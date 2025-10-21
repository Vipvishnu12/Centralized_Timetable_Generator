using System;
using Microsoft.Extensions.Configuration;
using Npgsql;

namespace Timetablegenerator.Connection
{
    public class DatabaseConnection
    {
        private readonly IConfiguration _configuration;


        public DatabaseConnection(IConfiguration configuration)
        {
            _configuration = configuration ?? throw new ArgumentNullException(nameof(configuration));
        }

        public NpgsqlConnection GetConnection()
        {
            String connectionString = _configuration.GetConnectionString("DefaultConnection")!;
            if (string.IsNullOrEmpty(connectionString))
            {
                throw new InvalidOperationException("Database connection String is missing in configuration");
            }
            return new NpgsqlConnection(connectionString);
        }

    }
}
