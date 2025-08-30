using Timetablegenerator.Connection;

var builder = WebApplication.CreateBuilder(args);

// Add services to the container.
builder.Services.AddControllers();
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen();

builder.Services.AddCors(options =>
{
    options.AddPolicy("AllowFrontend", policy =>
    {
        policy.WithOrigins("http://localhost:5173")  // Only allow frontend
              .AllowAnyHeader()
              .AllowAnyMethod();
        // Add .AllowCredentials() here if you're using cookies or auth headers
    });
});

// ✅ Register DatabaseConnection for DI
builder.Services.AddSingleton<DatabaseConnection>(provider =>
{
    var config = provider.GetRequiredService<IConfiguration>();
    return new DatabaseConnection(config);
});

var app = builder.Build();

// Middleware
if (app.Environment.IsDevelopment())
{
    app.UseSwagger();
    app.UseSwaggerUI();
}

app.UseHttpsRedirection();

// ✅ Apply CORS before routing
app.UseCors("AllowFrontend");

app.UseAuthorization();

app.MapControllers();

app.Run();
