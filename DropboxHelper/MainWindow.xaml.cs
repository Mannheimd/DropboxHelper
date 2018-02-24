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
        
        #region UI Inputs

        private void GetShareLink_Button_Click(object sender, RoutedEventArgs e)
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
                MessageBox.Show("This item has been deleted and cannot be shared. I don't even know why it's displaying here. I don't even know why I'm making this error, you should never see it...");
                return;
            }

            MessageBox.Show(selectedItem.Name);
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
