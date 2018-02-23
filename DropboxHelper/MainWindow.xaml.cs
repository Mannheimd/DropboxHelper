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

            DoTheThing();
        }

        public async void DoTheThing()
        {
            accessToken = await ReadAccessToken();

            SetupClient();

            await GetCurrentAccount(client);
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

        private async Task GetCurrentAccount(DropboxClient client)
        {
            var full = await client.Users.GetCurrentAccountAsync();

            string msg = "Located account";

            msg += string.Format("\nAccount id    : {0}", full.AccountId);
            msg += string.Format("\nCountry       : {0}", full.Country);
            msg += string.Format("\nEmail         : {0}", full.Email);
            msg += string.Format("\nIs paired     : {0}", full.IsPaired ? "Yes" : "No");
            msg += string.Format("\nLocale        : {0}", full.Locale);
            msg += string.Format("\nName");
            msg += string.Format("\n  Display  : {0}", full.Name.DisplayName);
            msg += string.Format("\n  Familiar : {0}", full.Name.FamiliarName);
            msg += string.Format("\n  Given    : {0}", full.Name.GivenName);
            msg += string.Format("\n  Surname  : {0}", full.Name.Surname);
            msg += string.Format("\nReferral link : {0}", full.ReferralLink);

            if (full.Team != null)
            {
                msg += string.Format("\nTeam");
                msg += string.Format("\n  Id   : {0}", full.Team.Id);
                msg += string.Format("\n  Name : {0}", full.Team.Name);
            }
            else
            {
                msg += string.Format("\nTeam - None");
            }

            MessageBox.Show(msg);
        }
    }
}
