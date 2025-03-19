using LocalDBTest.ViewModels;
using LocalDBTest.Services;

namespace LocalDBTest;

public partial class MainPage : ContentPage
{
    public MainPage(IDatabaseService liteDbService, ISQLiteDatabaseService sqliteService)
    {
        InitializeComponent();
        BindingContext = new DatabaseTestViewModel(liteDbService, sqliteService);
    }
}
