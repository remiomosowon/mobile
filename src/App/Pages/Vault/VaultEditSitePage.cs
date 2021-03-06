﻿using System;
using System.Collections.Generic;
using System.Linq;
using Acr.UserDialogs;
using Bit.App.Abstractions;
using Bit.App.Controls;
using Bit.App.Resources;
using Plugin.Connectivity.Abstractions;
using Xamarin.Forms;
using XLabs.Ioc;

namespace Bit.App.Pages
{
    public class VaultEditSitePage : ExtendedContentPage
    {
        private readonly string _siteId;
        private readonly ISiteService _siteService;
        private readonly IFolderService _folderService;
        private readonly IUserDialogs _userDialogs;
        private readonly IConnectivity _connectivity;
        private readonly IGoogleAnalyticsService _googleAnalyticsService;

        public VaultEditSitePage(string siteId)
        {
            _siteId = siteId;
            _siteService = Resolver.Resolve<ISiteService>();
            _folderService = Resolver.Resolve<IFolderService>();
            _userDialogs = Resolver.Resolve<IUserDialogs>();
            _connectivity = Resolver.Resolve<IConnectivity>();
            _googleAnalyticsService = Resolver.Resolve<IGoogleAnalyticsService>();

            Init();
        }

        public FormEntryCell PasswordCell { get; private set; }

        private void Init()
        {
            var site = _siteService.GetByIdAsync(_siteId).GetAwaiter().GetResult();
            if(site == null)
            {
                // TODO: handle error. navigate back? should never happen...
                return;
            }

            var notesCell = new FormEditorCell(height: 90);
            notesCell.Editor.Text = site.Notes?.Decrypt();
            PasswordCell = new FormEntryCell(AppResources.Password, IsPassword: true, nextElement: notesCell.Editor);
            PasswordCell.Entry.Text = site.Password?.Decrypt();
            var usernameCell = new FormEntryCell(AppResources.Username, nextElement: PasswordCell.Entry);
            usernameCell.Entry.Text = site.Username?.Decrypt();
            usernameCell.Entry.DisableAutocapitalize = true;
            usernameCell.Entry.Autocorrect = false;

            usernameCell.Entry.FontFamily = PasswordCell.Entry.FontFamily = Device.OnPlatform(
                iOS: "Courier", Android: "monospace", WinPhone: "Courier");

            var uriCell = new FormEntryCell(AppResources.URI, Keyboard.Url, nextElement: usernameCell.Entry);
            uriCell.Entry.Text = site.Uri?.Decrypt();
            var nameCell = new FormEntryCell(AppResources.Name, nextElement: uriCell.Entry);
            nameCell.Entry.Text = site.Name?.Decrypt();

            var generateCell = new ExtendedTextCell
            {
                Text = "Generate Password",
                ShowDisclousure = true
            };
            generateCell.Tapped += GenerateCell_Tapped; ;

            var folderOptions = new List<string> { AppResources.FolderNone };
            var folders = _folderService.GetAllAsync().GetAwaiter().GetResult()
                .OrderBy(f => f.Name?.Decrypt()).ToList();
            int selectedIndex = 0;
            int i = 0;
            foreach(var folder in folders)
            {
                i++;
                if(folder.Id == site.FolderId)
                {
                    selectedIndex = i;
                }

                folderOptions.Add(folder.Name.Decrypt());
            }
            var folderCell = new FormPickerCell(AppResources.Folder, folderOptions.ToArray());
            folderCell.Picker.SelectedIndex = selectedIndex;

            var favoriteCell = new ExtendedSwitchCell
            {
                Text = "Favorite",
                On = site.Favorite
            };

            var deleteCell = new ExtendedTextCell { Text = AppResources.Delete, TextColor = Color.Red };
            deleteCell.Tapped += DeleteCell_Tapped;

            var table = new ExtendedTableView
            {
                Intent = TableIntent.Settings,
                EnableScrolling = true,
                HasUnevenRows = true,
                Root = new TableRoot
                {
                    new TableSection("Site Information")
                    {
                        nameCell,
                        uriCell,
                        usernameCell,
                        PasswordCell,
                        generateCell
                    },
                    new TableSection
                    {
                        folderCell,
                        favoriteCell
                    },
                    new TableSection(AppResources.Notes)
                    {
                        notesCell
                    },
                    new TableSection
                    {
                        deleteCell
                    }
                }
            };

            if(Device.OS == TargetPlatform.iOS)
            {
                table.RowHeight = -1;
                table.EstimatedRowHeight = 70;
            }

            var saveToolBarItem = new ToolbarItem(AppResources.Save, null, async () =>
            {
                if(!_connectivity.IsConnected)
                {
                    AlertNoConnection();
                    return;
                }

                if(string.IsNullOrWhiteSpace(PasswordCell.Entry.Text))
                {
                    await DisplayAlert(AppResources.AnErrorHasOccurred, string.Format(AppResources.ValidationFieldRequired,
                        AppResources.Password), AppResources.Ok);
                    return;
                }

                if(string.IsNullOrWhiteSpace(nameCell.Entry.Text))
                {
                    await DisplayAlert(AppResources.AnErrorHasOccurred, string.Format(AppResources.ValidationFieldRequired,
                        AppResources.Name), AppResources.Ok);
                    return;
                }

                site.Uri = uriCell.Entry.Text?.Encrypt();
                site.Name = nameCell.Entry.Text?.Encrypt();
                site.Username = usernameCell.Entry.Text?.Encrypt();
                site.Password = PasswordCell.Entry.Text?.Encrypt();
                site.Notes = notesCell.Editor.Text?.Encrypt();
                site.Favorite = favoriteCell.On;

                if(folderCell.Picker.SelectedIndex > 0)
                {
                    site.FolderId = folders.ElementAt(folderCell.Picker.SelectedIndex - 1).Id;
                }
                else
                {
                    site.FolderId = null;
                }

                _userDialogs.ShowLoading("Saving...", MaskType.Black);
                var saveTask = await _siteService.SaveAsync(site);

                _userDialogs.HideLoading();

                if(saveTask.Succeeded)
                {
                    await Navigation.PopForDeviceAsync();
                    _userDialogs.Toast("Site updated.");
                    _googleAnalyticsService.TrackAppEvent("EditedSite");
                }
                else if(saveTask.Errors.Count() > 0)
                {
                    await _userDialogs.AlertAsync(saveTask.Errors.First().Message, AppResources.AnErrorHasOccurred);
                }
                else
                {
                    await _userDialogs.AlertAsync(AppResources.AnErrorHasOccurred);
                }
            }, ToolbarItemOrder.Default, 0);

            Title = "Edit Site";
            Content = table;
            ToolbarItems.Add(saveToolBarItem);
            if(Device.OS == TargetPlatform.iOS)
            {
                ToolbarItems.Add(new DismissModalToolBarItem(this, "Cancel"));
            }
        }

        protected override void OnAppearing()
        {
            base.OnAppearing();
            if(!_connectivity.IsConnected)
            {
                AlertNoConnection();
            }
        }

        private async void GenerateCell_Tapped(object sender, EventArgs e)
        {
            if(!string.IsNullOrWhiteSpace(PasswordCell.Entry.Text)
                && !await _userDialogs.ConfirmAsync("Are you sure you want to overwrite the current password?", null,
                AppResources.Yes, AppResources.No))
            {
                return;
            }

            var page = new ToolsPasswordGeneratorPage((password) =>
            {
                PasswordCell.Entry.Text = password;
                _userDialogs.Toast("Password generated.");
            });
            await Navigation.PushForDeviceAsync(page);
        }

        private async void DeleteCell_Tapped(object sender, EventArgs e)
        {
            if(!_connectivity.IsConnected)
            {
                AlertNoConnection();
                return;
            }

            if(!await _userDialogs.ConfirmAsync(AppResources.DoYouReallyWantToDelete, null, AppResources.Yes, AppResources.No))
            {
                return;
            }

            _userDialogs.ShowLoading("Deleting...", MaskType.Black);
            var deleteTask = await _siteService.DeleteAsync(_siteId);
            _userDialogs.HideLoading();

            if(deleteTask.Succeeded)
            {
                await Navigation.PopForDeviceAsync();
                _userDialogs.Toast("Site deleted.");
                _googleAnalyticsService.TrackAppEvent("DeletedSite");
            }
            else if(deleteTask.Errors.Count() > 0)
            {
                await _userDialogs.AlertAsync(deleteTask.Errors.First().Message, AppResources.AnErrorHasOccurred);
            }
            else
            {
                await _userDialogs.AlertAsync(AppResources.AnErrorHasOccurred);
            }
        }

        private void AlertNoConnection()
        {
            DisplayAlert(AppResources.InternetConnectionRequiredTitle, AppResources.InternetConnectionRequiredMessage,
                AppResources.Ok);
        }
    }
}
