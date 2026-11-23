using i25Core.AspNetCore;

// i25Core in one call: everything is configured in appsettings.json. See docs/library.md.
//
// Add your own services:      return I25CoreApp.Run(args, builder => builder.Services.AddScoped<IMyService, MyService>());
// Use your own database:      return I25CoreApp.Run<AppDbContext>(args);   (AppDbContext inherits I25DbContext)
return I25CoreApp.Run(args);
