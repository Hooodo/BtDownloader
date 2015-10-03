using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Collections.Specialized;
using System.Diagnostics;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Net;
using System.Net.Mime;
using System.Runtime.Remoting.Contexts;
using System.Text;
using System.Threading;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Navigation;
using System.Windows.Shapes;
using HtmlAgilityPack;
using WPF.Themes;

namespace BtDownloader
{
    /// <summary>
    /// Interaction logic for MainWindow.xaml
    /// </summary>
    public partial class MainWindow : Window
    {
        #region DEFS

        private string _url;
        private readonly string _baseUrl;
        private int _itemcount;
        private string _keyword;
        private int _selectedItem;
        private int _currentItem;
        private readonly int[] _fids;
        private int _selectedPage;
        private bool _isKeyword;
        private Thread _refreshThread;
        //private Thread _downloadThread;
        private const string DefaultUserAgent = "Mozilla/4.0 (compatible; MSIE 6.0; Windows NT 5.2; SV1; .NET CLR 1.1.4322; .NET CLR 2.0.50727)";
        private readonly ObservableCollection<ItemInfo> _gridItemInfos;
        private readonly ObservableCollection<ItemInfo> _tempItemInfos; 

        private delegate void AddGridList(ItemInfo itemInfo);
        private delegate void ShowTextInfo(string info);
        private delegate void UpdateButton(Button btn, bool state);
        private delegate void UpdateProgress();

        private readonly AddGridList _addGridList;
        private readonly ShowTextInfo _showTextInfo;
        private readonly UpdateButton _updateButton;
        private readonly UpdateProgress _updateProgress;

        #endregion
        public MainWindow()
        {
            InitializeComponent();

            #region INITAL VAR            

            _baseUrl = "http://cl.bearhk.info/";
            _url = _baseUrl + "thread0806.php?fid=15";
            _gridItemInfos = new ObservableCollection<ItemInfo>();
            _tempItemInfos = new ObservableCollection<ItemInfo>();
            _selectedItem = 0;
            _currentItem = 0;
            _selectedPage = 0;
            _fids = new[] {15, 2, 4, 5, 8};
            ContextMenu.PreviewMouseDown += ContextMenuOnPreviewMouseDown;
            _addGridList = AddGridItem;
            _showTextInfo = ShowInfo;
            _updateButton = UpdateBtnState;
            _updateProgress = UpdateProgressBar;

            TxtItemCount.Text = "50";
            //CbType.Text = "1";
            CbType.SelectedIndex = 0;
            CbThemes.ItemsSource = ThemeManager.GetThemes();
            CbThemes.SelectedIndex = 5;
            ProgressBarControl.Maximum = 100;
            ProgressBarControl.Minimum = 0;
            ProgressBarControl.Value = 0;
            DataGrid.DataContext = _gridItemInfos;
            _isKeyword = false;

            LoadSetting();

            #endregion
        }

        private void ContextMenuOnPreviewMouseDown(object sender, MouseButtonEventArgs mouseButtonEventArgs)
        {
            _selectedItem = DataGrid.SelectedIndex;
            Debug.WriteLine("[*] Selected index:" + _selectedItem);
            DownloadFromIndex(_selectedItem);
        }

        public void LoadSetting()
        {
            _itemcount = Int32.Parse(TxtItemCount.Text);
        }

        #region DELEGATE
        public void AddGridItem(ItemInfo itemInfo)
        {
            _gridItemInfos.Add(itemInfo);
        }

        public void ShowInfo(string info)
        {
            TxtInfo.Text = info;
            TxtInfo.ToolTip = info;
        }

        public void UpdateBtnState(Button btn, bool state)
        {
            btn.IsEnabled = state;
        }

        public void UpdateProgressBar()
        {
            ProgressBarControl.Value += 1;
            ProgressBarControl.ToolTip = String.Format("procesing {0}", ProgressBarControl.Value);
        }
        #endregion

        public string GetHtmlPage(string url)
        {
            HttpWebRequest request = WebRequest.Create(url) as HttpWebRequest;
            Debug.Assert(request != null, "request != null");
            request.Method = "GET";
            request.UserAgent = DefaultUserAgent;

            HttpWebResponse response = request.GetResponse() as HttpWebResponse;
            Debug.Assert(response != null, "response != null");
            Stream stream = response.GetResponseStream();
            Debug.Assert(stream != null, "stream != null");
            StreamReader readStream = new StreamReader(stream, Encoding.GetEncoding("gb2312"));

            string ret = readStream.ReadToEnd();

            response.Close();
            readStream.Close();
            request.Abort();

            return ret;
        }

        public void SaveHtmlPage(string url)
        {
            string html = GetHtmlPage(url);
            //Debug.Write(html);
            StreamWriter swWriter = new StreamWriter("tmp");
            swWriter.Write(html);
            swWriter.Close();
        }

        public int ParseSubjects(String page, int itemcount)
        {
            HtmlDocument document = new HtmlDocument();
            HtmlNode.ElementsFlags.Remove("form");
            //document.LoadHtml(page);
            document.LoadHtml(page);

            //HtmlNode nodeList = document.DocumentNode.SelectSingleNode("/html/body/div[2]/div[3]/table");
            int retcount = 0;
            HtmlNode nodeList = document.DocumentNode.SelectSingleNode("//*[@id=\"ajaxtable\"]");
            foreach (HtmlNode node in nodeList.ChildNodes[3].ChildNodes)
            {
                int count = node.ChildNodes.Count;
                HtmlNode innerNode = null;
                if (count > 3)
                    innerNode = node.ChildNodes[1];

                ItemInfo info = new ItemInfo();
                if (innerNode == null) continue;
                String[] titles = innerNode.InnerText.Trim().Split(new[] { '\r', '\n' });
                if (!titles[0].StartsWith("[") || titles[0].Contains("公告")) continue;
                //Debug.WriteLine(title);
                if ((_selectedPage == 4 || _selectedPage == 3) && titles.Length >= 3)
                {
                    info.Title = titles[0] + titles[2].Substring(1);
                }
                else
                    info.Title = titles[0];
                string href = innerNode.ChildNodes[1].InnerHtml;
                int index = href.IndexOf("href", StringComparison.Ordinal);
                int length = href.IndexOf('"', index + 6) - index - 6;
                href = href.Substring(index + 6, length);
                info.Link = href != String.Empty ? href : String.Empty;
                info.IsDown = true;
                Dispatcher.Invoke(_addGridList, info);
                retcount++;
                if (retcount == itemcount)
                    break;
            }
            return retcount;
        }

        public string ParseRmlink(String page)
        {
            int index = page.IndexOf("http://www.rmdown.com/link.php?hash=", StringComparison.Ordinal);
            if (index < 0)
                return String.Empty;

            int length = page.IndexOf('<', index) - index;

            return index == -1 ? string.Empty : page.Substring(index, length);
        }

        public void DownloadFromIndex(int index)
        {
            if (index < 0 || index > _gridItemInfos.Count || _gridItemInfos.Count == 0)
                return;

            _currentItem = index;
            ParameterizedThreadStart parameterizedThreadStart = new ParameterizedThreadStart(DownloadThread);
            Thread downloadThread = new Thread(parameterizedThreadStart);
            downloadThread.Start(index);            
        }

        public void DownloadBt(string url)
        {
            if (url == String.Empty)
                return;
            //Debug.WriteLine("[*] Current url:"+url);
            string html = GetHtmlPage(url);

            int index = html.IndexOf("name=\"ref\" value=", StringComparison.Ordinal);
            int length = html.IndexOf('"', index + 18) - index;
            string reff = html.Substring(index + 18, length - 18);
            //Debug.WriteLine(reff);
            index = html.IndexOf("NAME=\"reff\" value=", StringComparison.Ordinal);
            length = html.IndexOf('"', index + 19) - index;
            string refff = html.Substring(index + 19, length - 19);
            //Debug.WriteLine(refff);

            string boundry = "---------------------------" + DateTime.Now.Ticks.ToString("x");
            byte[] boundryBytes = Encoding.ASCII.GetBytes("\r\n--" + boundry + "\r\n");

            HttpWebRequest req = WebRequest.Create("http://www.rmdown.com/download.php") as HttpWebRequest;
            Debug.Assert(req != null, "req != null");
            CookieContainer cookieJar = new CookieContainer();
            req.ProtocolVersion = HttpVersion.Version11;
            req.Method = "POST";
            req.ServicePoint.Expect100Continue = false;
            req.Host = "www.rmdown.com";
            req.Referer = url;
            //req.ContentLength = postData.Length;
            req.ContentType = "multipart/form-data; boundary=" + boundry;
            req.AllowAutoRedirect = true;
            req.KeepAlive = true;
            req.Credentials = CredentialCache.DefaultCredentials;
            req.CookieContainer = cookieJar;
            req.UserAgent = DefaultUserAgent;
            req.Accept = "text/html,application/xhtml+xml,application/xml;q=0.9,*/*;q=0.8";
            req.Headers["Accept-Encoding"] = "gzip,deflate";
            req.Headers["Accept-Language"] = "zh-CN,zh;q=0.8";

            Stream rs = req.GetRequestStream();
            const string formdataTemplate = "Content-Disposition: form-data; name=\"{0}\"\r\n\r\n{1}";
            NameValueCollection nvc = new NameValueCollection { { "ref", reff }, { "reff", refff }, { "submit", "download" } };
            foreach (string key in nvc.Keys)
            {
                rs.Write(boundryBytes, 0, boundryBytes.Length);
                string formitem = string.Format(formdataTemplate, key, nvc[key]);
                byte[] formitemBytes = Encoding.UTF8.GetBytes(formitem);
                rs.Write(formitemBytes, 0, formitemBytes.Length);
            }
            rs.Write(boundryBytes, 0, boundryBytes.Length);

            byte[] trailer = Encoding.ASCII.GetBytes("\r\n--" + boundry + "--\r\n");
            rs.Write(trailer, 0, trailer.Length);
            rs.Close();

            HttpWebResponse response = (HttpWebResponse)req.GetResponse();
            Stream dataStream = response.GetResponseStream();
            Debug.Assert(dataStream != null, "dataStream != null");
            WebHeaderCollection wh = response.Headers;
            //foreach (string s in wh)
            //{
            //    Debug.WriteLine(s + ":" + wh[s]);
            //}
            string attach = "";
            if (wh.AllKeys.Contains("Content-Disposition"))
                attach = wh["Content-Disposition"];
            if (attach == "") return;
            int start = attach.IndexOf('"');
            attach = attach.Substring(start + 1, attach.LastIndexOf('"') - start - 1);
            Debug.WriteLine("[*] Filename:" + attach);

            //using (var fileStream = File.Create(attach))
            //{
            //    dataStream.CopyTo(fileStream);
            //}
            GZipStream gZipStream = new GZipStream(dataStream, CompressionMode.Decompress);
            using (var fileStream = File.Create(attach))
            {
                gZipStream.CopyTo(fileStream);
            }
            gZipStream.Close();

            req.Abort();
        }

        public void DownloadPic(string url, string dir)
        {
            if (url == String.Empty)
                return;

            string html = GetHtmlPage(url);
            string search = "type='image' src=";
            int start = 0;
            int count = 0;
            int index = html.IndexOf(search, start);
            while (index != -1)
            {
                string imageurl = html.Substring(index + 18, html.IndexOf("'", index + 18) - index - 18);
                Debug.WriteLine("[*] image: " + imageurl);

                using (System.Net.WebClient wc = new WebClient())
                {
                    wc.Headers.Add("User-Agent", DefaultUserAgent);
                    wc.DownloadFile(imageurl, string.Format("{0}\\{1}.{2}", dir, count, imageurl.Substring(imageurl.LastIndexOf('.') + 1)));
                }

                index = html.IndexOf(search, index + 18);
                count++;
            }
        }

        public void RefreshThread()
        {
            Dispatcher.Invoke(_updateButton, new object[] { BtnRefresh, false });
            Dispatcher.Invoke(_updateButton, new object[] { BtnDownload, false });
            try
            {
                int pagenum = 2;
                int count = ParseSubjects(GetHtmlPage(_url), _itemcount);
                while (count < _itemcount)
                {
                    Thread.Sleep(2000);
                    Debug.WriteLine("[*] Parse page {0}", pagenum);
                    count += ParseSubjects(GetHtmlPage(string.Format("{0}&search=&page={1}", _url, pagenum++)), _itemcount - count);
                }
                Debug.WriteLine("Request item count:{0},Show item count:{1}", _itemcount, count);
            }
            catch (WebException)
            {
                Dispatcher.Invoke(_showTextInfo, "Network exception");
            }
            catch (ArgumentOutOfRangeException)
            {
                Dispatcher.Invoke(_showTextInfo, "Analysis page failed");
            }
            catch (NullReferenceException)
            {
                Dispatcher.Invoke(_showTextInfo, "Log on failed");
            }
            Dispatcher.Invoke(_updateButton, new object[] { BtnRefresh, true });
            Dispatcher.Invoke(_updateButton, new object[] { BtnDownload, true });
        }

        public void DownloadThread(object index)
        {
            Dispatcher.Invoke(_updateButton, new object[] { BtnDownload, false });
            ItemInfo item = _isKeyword ? _tempItemInfos[(int)index] : _gridItemInfos[(int)index];
            if (item.Link == String.Empty || !item.IsDown)
            {
                Dispatcher.Invoke(_updateProgress, null);
                return;
            }
            string url = _baseUrl + item.Link;
            //Debug.WriteLine(String.Format("[*] item:{0},url:{1}", _currentItem, url));
            try
            {
                if (_selectedPage < 4)
                    DownloadBt(ParseRmlink(GetHtmlPage(url)));
                else if (_selectedPage == 4)
                {
                    Directory.CreateDirectory(String.Format("{0}\\{1}", Directory.GetCurrentDirectory(), item.Title));
                    DownloadPic(url, String.Format("{0}\\{1}", Directory.GetCurrentDirectory(), item.Title));
                }
                else ;
            }
            catch (Exception)
            {
                Debug.WriteLine("[!] Download failed");
                Dispatcher.Invoke(_showTextInfo, String.Format("Download {0} failed", (int)index));
            }
            
            Dispatcher.Invoke(_updateProgress, null);
            Dispatcher.Invoke(_updateButton, new object[] { BtnDownload, true });
        }

        private void BtnRefresh_OnClick(object sender, RoutedEventArgs e)
        {
            _gridItemInfos.Clear();
            _refreshThread = new Thread(RefreshThread);
            _refreshThread.Start();
        }

        private void BtnDownload_OnClick(object sender, RoutedEventArgs e)
        {
            ProgressBarControl.Value = 0;
            int count = _isKeyword ? _tempItemInfos.Count : _gridItemInfos.Count;
            ProgressBarControl.Maximum = count;
            for (int i = 0; i < count; i++)
            {
                DownloadFromIndex(i);
            }
        }

        private void BtnUnSelectAll_OnClick(object sender, RoutedEventArgs e)
        {
            foreach (var info in _gridItemInfos)
            {
                info.IsDown =! info.IsDown;
            }
        }

        private void BtnSelectAll_OnClick(object sender, RoutedEventArgs e)
        {
            foreach (var info in _gridItemInfos)
            {
                info.IsDown = true;
            }
        }

        private void TxtItemCount_TextChanged(object sender, TextChangedEventArgs e)
        {
            _itemcount = Int32.Parse(TxtItemCount.Text);
        }

        private void BtnUpdate_OnClick(object sender, RoutedEventArgs e)
        {
            _keyword = TxtKeyWord.Text;
            if (String.IsNullOrWhiteSpace(_keyword))
            {
                DataGrid.DataContext = _gridItemInfos;
                _isKeyword = false;
                return;
            }

            _tempItemInfos.Clear();
            foreach (ItemInfo info in _gridItemInfos)
            {
                if (info.Title.Contains(_keyword))
                    _tempItemInfos.Add(info);
            }
            DataGrid.DataContext = _tempItemInfos;
            _isKeyword = true;
        }

        private void BtnDeleteAll_OnClick(object sender, RoutedEventArgs e)
        {
            var btfiles = Directory.EnumerateFiles(Directory.GetCurrentDirectory(), "*.torrent");
            foreach (string btfile in btfiles)
            {
                File.Delete(btfile);
            }
        }

        private void CbType_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            _selectedPage = CbType.SelectedIndex;
            _url = String.Format("{0}thread0806.php?fid={1}", _baseUrl, _fids[_selectedPage]);
        }
    }
}
