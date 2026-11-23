using i25Core.Core.Mail;
using i25Core.Core.Notifications;
using i25Core.Infrastructure.Mail;
using i25Core.Infrastructure.Notifications;
using Microsoft.Extensions.Options;

namespace i25Core.AspNetCore.Mail
{
    public static class MailServiceExtensions
    {
        /// <summary>
        /// Registers IMailer and the transport chosen by "Mail:Driver" (Smtp, Log or None).
        /// Queued mail also needs AddI25BackgroundJobs.
        /// </summary>
        public static IServiceCollection AddI25Mail(
            this IServiceCollection services,
            IConfiguration configuration,
            IHostEnvironment environment)
        {
            var section = configuration.GetSection(MailOptions.SectionName);
            services.Configure<MailOptions>(section);
            services.PostConfigure<MailOptions>(o => o.RootPath = environment.ContentRootPath);

            var driver = (section.Get<MailOptions>() ?? new MailOptions()).Driver;

            // Fail at startup for a typo, not at the first email.
            switch (driver.ToLowerInvariant())
            {
                case "smtp":
                    services.AddSingleton<IEmailTransport, SmtpEmailTransport>();
                    break;
                case "log":
                    services.AddSingleton<IEmailTransport, LogEmailTransport>();
                    break;
                case "none":
                    services.AddSingleton<IEmailTransport, NullEmailTransport>();
                    break;
                default:
                    throw new InvalidOperationException(
                        $"Mail:Driver '{driver}' is not valid. Use Smtp, Log or None.");
            }

            services.AddSingleton<EmailTemplateRenderer>();
            services.AddSingleton<IMailer, Mailer>();
            services.AddTransient<QueuedMailRunner>();

            return services;
        }

        /// <summary>
        /// Registers INotificationService, INotificationInbox and the built-in "mail" and "database" channels.
        /// To add a channel, register your own INotificationChannel: <c>services.AddScoped&lt;INotificationChannel, SmsChannel&gt;()</c>.
        /// </summary>
        public static IServiceCollection AddI25Notifications(this IServiceCollection services)
        {
            services.AddScoped<INotificationChannel, MailNotificationChannel>();
            services.AddScoped<INotificationChannel, DatabaseNotificationChannel>();
            services.AddScoped<INotificationService, NotificationService>();
            services.AddScoped<INotificationInbox, NotificationInbox>();

            return services;
        }
    }
}
