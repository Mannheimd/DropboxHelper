using System;
using System.Collections.Generic;
using System.IO;
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

            await GetCurrentAccount(client);

            await ListFolder(client, "");
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

        private async Task<FullAccount> GetCurrentAccount(DropboxClient client)
        {
            return await client.Users.GetCurrentAccountAsync();
        }

        private async Task<List<Metadata>> GetFolderContent(DropboxClient client, string path, bool recursive = false)
        {
            ListFolderResult result = await client.Files.ListFolderAsync(path, recursive);
            List<Metadata> list = result.Entries.ToList();

            MessageBox.Show(list.Count.ToString());

            while (result.HasMore)
            {
                result = await client.Files.ListFolderContinueAsync(result.Cursor);
                list.AddRange(result.Entries.ToList());
            }

            return list;
        }
    }
}
