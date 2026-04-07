using System.Collections.Specialized;
using System.Windows.Controls;

namespace SentinelStream.App.Views;

public partial class WarRoomView : UserControl
{
    public WarRoomView()
    {
        InitializeComponent();
        ((INotifyCollectionChanged)LogList.Items).CollectionChanged += LogList_CollectionChanged;
    }

    private void LogList_CollectionChanged(object? sender, NotifyCollectionChangedEventArgs e)
    {
        if (e.Action == NotifyCollectionChangedAction.Add && LogList.Items.Count > 0)
        {
            LogList.ScrollIntoView(LogList.Items[LogList.Items.Count - 1]);
        }
    }
}
