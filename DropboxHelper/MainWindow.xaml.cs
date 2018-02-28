using System;
using System.Collections.Generic;
using System.IO;
using System.Globalization;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Navigation;
using System.Windows.Shapes;
using System.Net.Http;
using Dropbox.Api;
using Dropbox.Api.Files;
using Dropbox.Api.Users;
using Dropbox.Api.Sharing;

namespace DropboxHelper
{
    /// <summary>
    /// Interaction logic for MainWindow.xaml
    /// </summary>
    public partial class MainWindow : Window
    {
        public MainWindow()
        {
            InitializeComponent();

            DoTheThing();
        }

        public async void DoTheThing()
        {
            client = DropboxHandler.SetupClient(await DropboxHandler.ReadAccessToken());

            await ChangeToFolder(client, "");
        }

        DropboxClient client;

        private async Task ChangeToFolder(DropboxClient client, string path, bool recursive = false)
        {
            DropboxFolderContent.ItemsSource = await DropboxHandler.GetFolderContent(client, path, recursive);
            CurrentDirectoryPath_Label.Content = path;
        }

        #region UI Inputs

        private async void GetShareLink_Button_Click(object sender, RoutedEventArgs e)
        {
            Metadata selectedItem = DropboxFolderContent.SelectedIndex > -1 ? ((Metadata)DropboxFolderContent.SelectedItem) : null;

            if (selectedItem == null)
                return;

            if (selectedItem.IsFolder)
            {
                MessageBox.Show("This is a folder. Please select a file to share.");
                return;
            }

            if (selectedItem.IsDeleted)
            {
                MessageBox.Show("This item has been deleted and cannot be shared. I don't even know why it's displaying here. I don't even know why I'm making this error, you should never see it... In fact it might be possible to share the file, I haven't checked. I'm just not going to let you try.");
                return;
            }

            SharedLinkMetadata share = await DropboxHandler.ShareFile(client, selectedItem, RequestedVisibility.Password.Instance, "password");

            try
            {
                Clipboard.SetDataObject(share.Url);
            }
            catch (Exception error)
            {
                MessageBox.Show("Unable to copy to clipboard. This can be caused by a an active WebEx session interfering with clipboard operations. Try again after closing your WebEx session.\n\n" + error.Message);
            }
        }

        private async void DropboxFolderContent_ListView_MouseDoubleClick(object sender, MouseButtonEventArgs e)
        {
            Metadata selectedItem = DropboxFolderContent.SelectedIndex > -1 ? ((Metadata)DropboxFolderContent.SelectedItem) : null;

            if (selectedItem == null)
                return;

            if (selectedItem.IsFolder)
            {
                await ChangeToFolder(client, selectedItem.PathLower);
            }
        }

        private async void Up_Button_Click(object sender, RoutedEventArgs e)
        {
            string currentPath = CurrentDirectoryPath_Label.Content.ToString();

            if (!currentPath.Contains('/'))
                return;

            int slashPos = currentPath.LastIndexOf('/');
            
            string newPath = currentPath.Substring(0, slashPos);

            await ChangeToFolder(client, newPath);
        }

        #endregion
    }

    public class DropboxHandler
    {
        public static DropboxClient SetupClient(string accessToken)
        {
            try
            {
                DropboxClientConfig config = new DropboxClientConfig();
                config.HttpClient = new HttpClient();

                return new DropboxClient(accessToken, config);
            }
            catch (HttpException e)
            {
                string msg = "Exception reported from RPC layer";
                msg += string.Format("\n    Status code: {0}", e.StatusCode);
                msg += string.Format("\n    Message    : {0}", e.Message);
                if (e.RequestUri != null)
                {
                    msg += string.Format("\n    Request uri: {0}", e.RequestUri);
                }
                MessageBox.Show(msg);

                return null;
            }
        }

        public static async Task<string> ReadAccessToken()
        {
            string file = AppDomain.CurrentDomain.BaseDirectory + @"\accesstoken.txt";

            if (!File.Exists(file))
                return null;

            try
            {
                using (StreamReader reader = File.OpenText(file))
                {
                    return reader.ReadToEndAsync().Result;
                }
            }
            catch
            {
                return null;
            }
        }

        public static async Task<List<Metadata>> GetFolderContent(DropboxClient client, string path, bool recursive)
        {
            ListFolderResult result = await client.Files.ListFolderAsync(path, recursive);
            List<Metadata> list = result.Entries.ToList();

            while (result.HasMore)
            {
                result = await client.Files.ListFolderContinueAsync(result.Cursor);
                list.AddRange(result.Entries.ToList());
            }

            return list;
        }

        public static async Task<SharedLinkMetadata> ShareFile(DropboxClient client, Metadata file, RequestedVisibility requestedVisibility, string password = null, bool forceNewLink = false)
        {
            SharedLinkMetadata existingLink = await GetFileShareLink(client, file);
            if (existingLink != null)
            {
                if (forceNewLink)
                {
                    await RevokeFileShareLink(client, existingLink.Url);
                }
                else if (!IsMoreVisiblePermission(existingLink, requestedVisibility))
                {
                    return await ChangeShareLinkPermissions(client, existingLink.Url, requestedVisibility);
                }
                else return existingLink;
            }

            return await CreateFileShareLink(client, file, requestedVisibility, password);
        }

        /// <summary>
        /// Checks if link has an equal or more 'visible' permission than requested visibility
        /// TeamOnly < Password < Public
        /// </summary>
        /// <param name="link"></param>
        /// <param name="requestedVisibility"></param>
        /// <returns></returns>
        public static bool IsMoreVisiblePermission(SharedLinkMetadata link, RequestedVisibility requestedVisibility)
        {
            if (ConvertResolvedVisibilityToInt(link.LinkPermissions.ResolvedVisibility)
                >= ConvertRequestedVisibilityToInt(requestedVisibility))
                return true;
            else
                return false;
        }

        public static int ConvertResolvedVisibilityToInt(ResolvedVisibility visibility)
        {
            if (visibility.IsTeamOnly
                || visibility.IsTeamAndPassword
                || visibility.IsOther
                || visibility.IsSharedFolderOnly)
                return 0;
            else if (visibility.IsPassword)
                return 1;
            else if (visibility.IsPublic)
                return 2;
            else
                return 0;
        }

        public static int ConvertRequestedVisibilityToInt(RequestedVisibility visibility)
        {
            if (visibility.IsTeamOnly)
                return 0;
            else if (visibility.IsPassword)
                return 1;
            else if (visibility.IsPublic)
                return 2;
            else
                return 0;
        }

        public static async Task<SharedLinkMetadata> CreateFileShareLink(DropboxClient client, Metadata file, RequestedVisibility requestedVisibility, string password)
        {
            SharedLinkSettings settings = new SharedLinkSettings();
            if (requestedVisibility.IsPassword)
            {
                if (password == null)
                {
                    //TODO: Add error handling
                    return new SharedLinkMetadata();
                }
                settings = new SharedLinkSettings(requestedVisibility, password);
            }
            else
            {
                settings = new SharedLinkSettings(requestedVisibility);
            }
            
            CreateSharedLinkWithSettingsArg arg = new CreateSharedLinkWithSettingsArg(file.PathLower, settings);
            try
            {
                return await client.Sharing.CreateSharedLinkWithSettingsAsync(arg);
            }
            catch (ApiException<CreateSharedLinkWithSettingsError> error)
            {
                //TODO: Add error handling
                return new SharedLinkMetadata();
            }
        }

        public static async Task<SharedLinkMetadata> GetFileShareLink(DropboxClient client, Metadata file)
        {
            ListSharedLinksResult sharedLinks;
            try
            {
                sharedLinks = await client.Sharing.ListSharedLinksAsync(file.PathLower);
            }
            catch (ApiException<GetSharedLinkFileError> error)
            {
                //TODO: Add error handling
                return null;
            }

            if (sharedLinks.Links.Count < 1)
            {
                //TODO: Add error handling
                return null;
            }

            if (sharedLinks.Links[0] == null)
            {
                //TODO: Add error handling
                return null;
            }

            return sharedLinks.Links[0];
        }

        public static async Task RevokeFileShareLink(DropboxClient client, string url)
        {
            try
            {
                await client.Sharing.RevokeSharedLinkAsync(url);
            }
            catch (ApiException<RevokeSharedLinkError> error)
            {
                //TODO: Add error handling
            }
        }

        public static async Task<SharedLinkMetadata> GetShareLinkMetadata(DropboxClient client, string url, string password)
        {
            try
            {
                return await client.Sharing.GetSharedLinkMetadataAsync(url);
            }
            catch (ApiException<SharedLinkError> error)
            {
                //TODO: Add error handling
                return new SharedLinkMetadata();
            }
        }

        public static async Task<SharedLinkMetadata> ChangeShareLinkPermissions(DropboxClient client, string url, RequestedVisibility requestedVisibility)
        {
            SharedLinkSettings settings = new SharedLinkSettings(requestedVisibility);

            try
            {
                return await client.Sharing.ModifySharedLinkSettingsAsync(url, settings);
            }
            catch (ApiException<ModifySharedLinkSettingsError> error)
            {
                //TODO: Add error handling
                return new SharedLinkMetadata();
            }
        }
    }

    #region Converters

    public class BoolToFileFolder_Converter : IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        {
            if (value is bool && (bool)value == true)
            {
                return "File";
            }
            else if (value is bool && (bool)value == false)
            {
                return "Folder";
            }

            return "Unrecognised";
        }

        public object ConvertBack(object value, Type targetType, object Parameter, CultureInfo culture)
        {
            throw new Exception("This method is not implemented.");
        }
    }

    #endregion
}
