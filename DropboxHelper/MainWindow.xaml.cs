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
            accessToken = await ReadAccessToken();

            SetupClient();

            DropboxFolderContent.ItemsSource = await GetFolderContent(client, "");
        }

        string accessToken;
        DropboxClient client;

        private void SetupClient()
        {
            try
            {
                DropboxClientConfig config = new DropboxClientConfig();
                config.HttpClient = new HttpClient();

                client = new DropboxClient(accessToken, config);
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
            }
        }

        private async Task<string> ReadAccessToken()
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

        private async Task<List<Metadata>> GetFolderContent(DropboxClient client, string path, bool recursive = false)
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

        private async Task<SharedLinkMetadata> CreateFileShareLink(DropboxClient client, Metadata file, string password = null, bool forceNewLink = false)
        {
            if (password != null)
            {
                SharedLinkSettings settings = new SharedLinkSettings(new RequestedVisibility().AsPassword, password);
            }
            else
            {
                SharedLinkSettings settings = new SharedLinkSettings(new RequestedVisibility().AsPublic);
            }
            CreateSharedLinkWithSettingsArg arg = new CreateSharedLinkWithSettingsArg(file.PathLower);
            try
            {
                return await client.Sharing.CreateSharedLinkWithSettingsAsync(arg);
            }
            catch(ApiException<CreateSharedLinkWithSettingsError> error)
            {
                if (error.ErrorResponse.IsSharedLinkAlreadyExists)
                {
                    SharedFileMetadata currentShare = await GetFileShareLink(client, file);
                    if (forceNewLink)
                    {
                        await RevokeFileShareLink(client, currentShare.PreviewUrl);
                        try
                        {
                            return await client.Sharing.CreateSharedLinkWithSettingsAsync(arg);
                        }
                        catch (ApiException<CreateSharedLinkWithSettingsError> error2)
                        {
                            MessageBox.Show("1 " + error2.ErrorResponse + " " + error2.Message);
                            //TODO: Add error handling
                            return new SharedLinkMetadata();
                        }
                    }
                    else
                    {
                        return await GetShareLinkMetadata(client, currentShare.PreviewUrl, password);
                    }
                }
                else
                {
                    MessageBox.Show("2 " + error.ErrorResponse + " " + error.Message);
                    //TODO: Add error handling
                    return new SharedLinkMetadata();
                }
            }
        }

        private async Task<SharedFileMetadata> GetFileShareLink(DropboxClient client, Metadata file)
        {
            try
            {
                return await client.Sharing.GetFileMetadataAsync(file.PathLower);
            }
            catch (ApiException<GetSharedLinkFileError> error)
            {
                MessageBox.Show("3 " + error.ErrorResponse + " " + error.Message);
                //TODO: Add error handling
                return new SharedFileMetadata();
            }
        }

        private async Task RevokeFileShareLink(DropboxClient client, string url)
        {
            try
            {
                await client.Sharing.RevokeSharedLinkAsync(url);
            }
            catch (ApiException<RevokeSharedLinkError> error)
            {
                MessageBox.Show("4 " + error.ErrorResponse + " " + error.Message);
                //TODO: Add error handling
            }
        }

        private async Task<SharedLinkMetadata> GetShareLinkMetadata(DropboxClient client, string url, string password)
        {
            try
            {
                return await client.Sharing.GetSharedLinkMetadataAsync(url);
            }
            catch (ApiException<SharedLinkError> error)
            {
                MessageBox.Show("5 " + error.ErrorResponse + " " + error.Message);
                //TODO: Add error handling
                return new SharedLinkMetadata();
            }
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

            SharedLinkMetadata shareMetadata = await CreateFileShareLink(client, selectedItem);

            MessageBox.Show(shareMetadata.Url + "\n" + shareMetadata.LinkPermissions.ResolvedVisibility.IsPassword);
        }

        private async void DropboxFolderContent_ListView_MouseDoubleClick(object sender, MouseButtonEventArgs e)
        {
            Metadata selectedItem = DropboxFolderContent.SelectedIndex > -1 ? ((Metadata)DropboxFolderContent.SelectedItem) : null;

            if (selectedItem == null)
                return;

            if (selectedItem.IsFolder)
            {
                DropboxFolderContent.ItemsSource = await GetFolderContent(client, selectedItem.PathLower);
            }
        }

        #endregion
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
