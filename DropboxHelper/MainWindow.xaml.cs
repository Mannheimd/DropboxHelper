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

            MessageBox.Show(GetAccessToken().Result);
        }

        private async Task<string> GetAccessToken()
        {
            byte[] token;

            using (FileStream Stream = File.OpenRead(AppDomain.CurrentDomain.BaseDirectory + @"\accesstoken.txt"))
            {
                token = new byte[Stream.Length];
                await Stream.ReadAsync(token, 0, (int)Stream.Length);
            }

            return Encoding.ASCII.GetString(token);
        }
    }
}
