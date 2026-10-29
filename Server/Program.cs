using System.Text;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.EntityFrameworkCore;
using Microsoft.IdentityModel.Tokens;
using Microsoft.OpenApi.Models;
using Server.Data;
using Server.Helpers;
using Server.Middleware;
using Server.Services;

var builder = WebApplication.CreateBuilder(args);

// Configure listening URL
builder.WebHost.UseUrls("http://0.0.0.0:5057");

// Add services to the container
builder.Services.AddControllers();
builder.Services.AddMemoryCache();

// Configure DbContext
builder.Services.AddDbContext<ApplicationDbContext>(options =>
    options.UseSqlServer(builder.Configuration.GetConnectionString("DefaultConnection"), sqlOptions => sqlOptions.EnableRetryOnFailure()));

// Configure JWT Authentication
var jwtSettings = builder.Configuration.GetSection("Jwt");
var key = Encoding.UTF8.GetBytes(jwtSettings["Key"]!);

builder.Services.AddAuthentication(options =>
{
    options.DefaultAuthenticateScheme = JwtBearerDefaults.AuthenticationScheme;
    options.DefaultChallengeScheme = JwtBearerDefaults.AuthenticationScheme;
})
.AddJwtBearer(options =>
{
    options.TokenValidationParameters = new TokenValidationParameters
    {
        ValidateIssuer = true,
        ValidateAudience = true,
        ValidateLifetime = true,
        ValidateIssuerSigningKey = true,
        ValidIssuer = jwtSettings["Issuer"],
        ValidAudience = jwtSettings["Audience"],
        IssuerSigningKey = new SymmetricSecurityKey(key),
        ClockSkew = TimeSpan.Zero
    };
});

builder.Services.AddAuthorization();

// Register services
builder.Services.AddScoped<IAuthService, AuthService>();
builder.Services.AddScoped<IDeviceService, DeviceService>();
builder.Services.AddScoped<IOrderService, OrderService>();
builder.Services.AddScoped<IReviewService, ReviewService>();
builder.Services.AddScoped<IEmailService, ResendEmailService>();
builder.Services.AddSingleton<IVideoCallService, LiveKitService>();
builder.Services.AddSingleton<ILocationService, LocationService>();
builder.Services.AddHttpClient<ResendEmailService>();
builder.Services.AddScoped<JwtHelper>();

// Configure CORS
builder.Services.AddCors(options =>
{
    options.AddPolicy("AllowAll", policy =>
    {
        policy.AllowAnyOrigin()
              .AllowAnyMethod()
              .AllowAnyHeader();
    });
});

// Configure Swagger
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen(c =>
{
    c.SwaggerDoc("v1", new OpenApiInfo
    {
        Title = "Child Devices Management API",
        Version = "v1",
        Description = "API quản lý thiết bị trẻ em - Đăng ký & Đăng nhập hoàn toàn qua Email",
        Contact = new OpenApiContact
        {
            Name = "Child Devices Team",
            Email = "support@zeion.online"
        }
    });

    // Add JWT Authentication to Swagger
    c.AddSecurityDefinition("Bearer", new OpenApiSecurityScheme
    {
        Description = "JWT Authorization header using the Bearer scheme. Example: \"Bearer {token}\"",
        Name = "Authorization",
        In = ParameterLocation.Header,
        Type = SecuritySchemeType.ApiKey,
        Scheme = "Bearer"
    });

    c.AddSecurityRequirement(new OpenApiSecurityRequirement
    {
        {
            new OpenApiSecurityScheme
            {
                Reference = new OpenApiReference
                {
                    Type = ReferenceType.SecurityScheme,
                    Id = "Bearer"
                }
            },
            Array.Empty<string>()
        }
    });
});

var app = builder.Build();

// Configure the HTTP request pipeline
app.UseMiddleware<ErrorHandlingMiddleware>();

// Enable Swagger UI in all environments (Development & Production/Docker)
app.UseSwagger();
app.UseSwaggerUI(c =>
{
    c.SwaggerEndpoint("/swagger/v1/swagger.json", "Child Devices Management API V1");
    c.RoutePrefix = string.Empty; // Swagger UI at root
});

app.UseCors("AllowAll");

app.UseAuthentication();
app.UseAuthorization();

app.MapControllers();

// Auto-migrate database on startup and seed 3 default role accounts
using (var scope = app.Services.CreateScope())
{
    var dbContext = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
    try
    {
        dbContext.Database.Migrate();

        var defaultUsers = new[]
        {
            (Email: "anhjkr@gmail.com", LegacyUsername: "anhjkr", FullName: "Hồng Hoàng Khang (User)", Role: Server.Models.UserRole.User),
            (Email: "anhjkrr@gmail.com", LegacyUsername: "anhjkrr", FullName: "Hồng Hoàng Khang (Staff)", Role: Server.Models.UserRole.Staff),
            (Email: "anhjkrrr@gmail.com", LegacyUsername: "anhjkrrr", FullName: "Hồng Hoàng Khang (Admin)", Role: Server.Models.UserRole.Admin),
            (Email: "honghoangkhang@gmail.com", LegacyUsername: "honghoangkhang", FullName: "Hồng Hoàng Khang", Role: Server.Models.UserRole.User),
            (Email: "honghoangkhang.staff@gmail.com", LegacyUsername: "khangstaff", FullName: "Hồng Hoàng Khang (Staff)", Role: Server.Models.UserRole.Staff),
            (Email: "honghoangkhang.admin@gmail.com", LegacyUsername: "khangadmin", FullName: "Hồng Hoàng Khang (Admin)", Role: Server.Models.UserRole.Admin),
        };

        bool hasChanges = false;
        foreach (var item in defaultUsers)
        {
            var existingByEmail = dbContext.Users.FirstOrDefault(u => u.Email.ToLower() == item.Email);
            var existingLegacy = dbContext.Users.FirstOrDefault(u => u.Email.ToLower() == item.LegacyUsername);

            if (existingByEmail == null && existingLegacy != null)
            {
                existingLegacy.Email = item.Email;
                existingByEmail = existingLegacy;
                hasChanges = true;
            }
            else if (existingByEmail != null && existingLegacy != null && existingByEmail.Id != existingLegacy.Id)
            {
                dbContext.Users.Remove(existingLegacy);
                hasChanges = true;
            }

            if (existingByEmail == null)
            {
                dbContext.Users.Add(new Server.Models.User
                {
                    Email = item.Email,
                    PasswordHash = BCrypt.Net.BCrypt.HashPassword("Anhhung1@"),
                    FullName = item.FullName,
                    PhoneNumber = "0901234567",
                    Role = item.Role,
                    CreatedAt = DateTime.UtcNow,
                    IsActive = true
                });
                hasChanges = true;
            }
            else if (existingByEmail.Email != item.Email || existingByEmail.FullName != item.FullName || existingByEmail.Role != item.Role || !BCrypt.Net.BCrypt.Verify("Anhhung1@", existingByEmail.PasswordHash) || !existingByEmail.IsActive)
            {
                existingByEmail.Email = item.Email; existingByEmail.FullName = item.FullName;
                existingByEmail.Role = item.Role;
                existingByEmail.PasswordHash = BCrypt.Net.BCrypt.HashPassword("Anhhung1@");
                existingByEmail.IsActive = true;
                existingByEmail.UpdatedAt = DateTime.UtcNow;
                hasChanges = true;
            }
        }

        if (hasChanges)
        {
            dbContext.SaveChanges();
        }

        Console.WriteLine("Database migrated and default accounts seeded successfully");
    }
    catch (Exception ex)
    {
        Console.WriteLine($"Error migrating database: {ex.Message}");
    }
}

app.Run();
