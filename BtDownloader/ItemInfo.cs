using System.ComponentModel;
using BtDownloader.Annotations;

namespace BtDownloader
{
    public class ItemInfo : INotifyPropertyChanged
    {
        public string Title { get; set; }
        public string Link { get; set; }
        public string DownLink { get; set; }
        private bool _isDown;

        public bool IsDown
        {
            get { return _isDown; }
            set
            {
                if (_isDown != value)
                {
                    _isDown = value;
                    OnPropertyChanged("IsDown");
                }
            }
        }

        public event PropertyChangedEventHandler PropertyChanged;

        [NotifyPropertyChangedInvocator]
        protected virtual void OnPropertyChanged(string propertyName)
        {
            PropertyChangedEventHandler handler = PropertyChanged;
            if (handler != null) handler(this, new PropertyChangedEventArgs(propertyName));
        }
    }
}
