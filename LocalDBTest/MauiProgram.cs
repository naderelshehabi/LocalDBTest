using Microsoft.Extensions.Logging;
using LocalDBTest.Services;

namespace LocalDBTest;

public static class MauiProgram
{
	public static MauiApp CreateMauiApp()
	{
		var builder = MauiApp.CreateBuilder();
		builder
			.UseMauiApp<App>()
			.ConfigureFonts(fonts =>
			{
				fonts.AddFont("OpenSans-Regular.ttf", "OpenSansRegular");
				fonts.AddFont("OpenSans-Semibold.ttf", "OpenSansSemibold");
			});

		// Initialize SQLite
		SQLitePCL.Batteries.Init();

		builder.Services.AddSingleton<IDatabaseService, LiteDatabaseService>();
		builder.Services.AddSingleton<ISQLiteDatabaseService, SQLiteDatabaseService>();

#if DEBUG
		builder.Logging.AddDebug();
#endif

		return builder.Build();
	}
}
