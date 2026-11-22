using i25Core.BusinessLayer.Implementation;
using i25Core.BusinessLayer.Implementation.Base;
using i25Core.BusinessLayer.Implementation.Interface;
using i25Core.Infrastructure.Extensions;
using i25Core.Core.DTO;
using i25Core.Core.Entities;
using Microsoft.AspNetCore.Mvc;
using FluentValidation;
using i25Core.Web.Validator.Extensions;
using i25Core.Web.Validator.Base;
using i25Core.Web.Middleware;

var builder = WebApplication.CreateBuilder(args);

// Add services to the container.

// Add your infrastructure services here:
builder.Services.AddI25Infrastructure(builder.Configuration.GetConnectionString("DefaultConnection")!);

builder.Services.AddScoped<IRepository, BaseRepository>();
builder.Services.AddScoped(typeof(IBusinessLayer<>), typeof(BaseBusinessLayer<>));
builder.Services.AddScoped<IAuditLogBusinessLayer, AuditLogBusinessLayerLayer>();


//Validators
builder.Services.AddFoundationValidators(typeof(BaseValidator).Assembly);

// Keep automatic [ApiController] model validation responses consistent
// with the application's standard API response envelope.
builder.Services.Configure<ApiBehaviorOptions>(options =>
{
    options.InvalidModelStateResponseFactory = context =>
    {
        var errors = context.ModelState
            .Where(entry => entry.Value?.Errors.Count > 0)
            .ToDictionary(
                entry => entry.Key,
                entry => entry.Value!.Errors
                    .Select(error => string.IsNullOrWhiteSpace(error.ErrorMessage)
                        ? "The supplied value is invalid."
                        : error.ErrorMessage)
                    .ToArray());

        var response = ApiResponse<object?>.Failure(
            "Validation failed.",
            errors);

        return new BadRequestObjectResult(response);
    };
});

builder.Services.AddControllers();
// Learn more about configuring Swagger/OpenAPI at https://aka.ms/aspnetcore/swashbuckle
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen();

// Global exception handling: every unhandled exception becomes an ApiResponse.
builder.Services.AddExceptionHandler<GlobalExceptionHandler>();
builder.Services.AddProblemDetails();

var app = builder.Build();

app.UseExceptionHandler();

// Configure the HTTP request pipeline.
if (app.Environment.IsDevelopment())
{
    app.UseSwagger();
    app.UseSwaggerUI();
}

app.UseHttpsRedirection();

app.UseAuthorization();

app.MapControllers();

app.Run();
