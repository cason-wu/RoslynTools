using System.Windows.Controls;
using System.Windows.Input;
using RoslynTree.ViewModels;

namespace RoslynTree.Views;

public partial class ProjectBrowserView : UserControl
{
    public ProjectBrowserView()
    {
        InitializeComponent();
    }

    private void TreeViewItem_MouseLeftButtonUp(object sender, MouseButtonEventArgs e)
    {
        if (sender is not TreeViewItem item) return;
        if (DataContext is not MainViewModel vm) return;

        switch (item.DataContext)
        {
            case NodeTypeViewModel entry:
                vm.SelectEntryCommand.Execute(entry);
                e.Handled = true;
                break;
            case NodeMemberViewModel member:
                vm.SelectMemberCommand.Execute(member);
                e.Handled = true;
                break;
        }
    }
}
