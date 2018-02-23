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
using Dropbox.Api;
using System.Net.Http;

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

            accessToken = ReadAccessToken().Result;
        }

        string accessToken;
        DropboxClient client;

        private void SetupClient()
        {
            DropboxClientConfig config = new DropboxClientConfig();
            config.HttpClient = new HttpClient();

            client = new DropboxClient(accessToken, config);
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
    }
}
