using Microsoft.AspNetCore.Mvc;
using Npgsql;
using Microsoft.Extensions.Configuration;
using System.Collections.Generic;

namespace YourNamespace.Controllers
{
    [ApiController]
    [Route("api/[controller]")]
    public class LoginController : ControllerBase
    {
        private readonly IConfiguration _configuration;

        public LoginController(IConfiguration configuration)
        {
            _configuration = configuration;
        }

        // ✅ Login verification
        [HttpPost("verify")]
        public IActionResult Verify([FromBody] LoginDto dto)
        {
            try
            {
                var connectionString = _configuration.GetConnectionString("DefaultConnection");

                using var conn = new NpgsqlConnection(connectionString);
                conn.Open();

                var query = @"
        SELECT username,role 
        FROM login 
        WHERE username = @username AND password = @password
    ";

                using var cmd = new NpgsqlCommand(query, conn);
                cmd.Parameters.AddWithValue("@username", dto.Username.ToUpper());
                cmd.Parameters.AddWithValue("@password", dto.Password);

                using var reader = cmd.ExecuteReader();
                if (reader.Read())
                {
                    var deptName = reader.GetString(0);
                    var role = reader.GetString(1); // Reading the role

                    return Ok(new
                    {
                        message = "Login successful",
                        username = dto.Username.ToUpper(),
                        departmentName = deptName,
                        role = role
                    });
                }
                else
                {
                    return Unauthorized(new { message = "Invalid department ID or password." });
                }
            }
            catch (Exception ex)
            {
                return StatusCode(500, new { message = "Server error", error = ex.Message });
            }
        }
        [HttpPost("add-department")]
        public IActionResult AddDepartment([FromBody] DepartmentDto dept)
        {
            if (dept == null || string.IsNullOrWhiteSpace(dept.DepartmentId) ||
                string.IsNullOrWhiteSpace(dept.DepartmentName) ||
                string.IsNullOrWhiteSpace(dept.Password) ||
                string.IsNullOrWhiteSpace(dept.role))
            {
                return BadRequest(new { message = "All fields are required." });
            }

            try
            {
                var connectionString = _configuration.GetConnectionString("DefaultConnection");

                using var conn = new NpgsqlConnection(connectionString);
                conn.Open();

                using var transaction = conn.BeginTransaction();

                var adminQuery = @"
            INSERT INTO admin (department_id, department_name)
            VALUES (@DepartmentId, @DepartmentName)
        ";

                using (var cmd = new NpgsqlCommand(adminQuery, conn, transaction))
                {
                    cmd.Parameters.AddWithValue("@DepartmentId", dept.DepartmentId.ToUpper());
                    cmd.Parameters.AddWithValue("@DepartmentName", dept.DepartmentName.ToUpper());
                    cmd.ExecuteNonQuery();
                }

                var loginQuery = @"
            INSERT INTO login (username, password, role)
            VALUES (@DepartmentId, @Password, @Role)
        ";

                using (var cmd1 = new NpgsqlCommand(loginQuery, conn, transaction))
                {
                    cmd1.Parameters.AddWithValue("@DepartmentId", dept.DepartmentId.ToUpper());
                    cmd1.Parameters.AddWithValue("@Password", dept.Password);
                    cmd1.Parameters.AddWithValue("@Role", dept.role);
                    cmd1.ExecuteNonQuery();
                }

                transaction.Commit();

                return Ok(new { message = "Admin department added successfully." });
            }
            catch (PostgresException ex) when (ex.SqlState == "23505")
            {
                return Conflict(new { message = "Department or username already exists." });
            }
            catch (Exception ex)
            {
                return StatusCode(500, new { message = "Server error", error = ex.Message });
            }
        }

        public class DepartmentDto
        {
            public string DepartmentId { get; set; }
            public string DepartmentName { get; set; }
            //    public string Block { get; set; }
            public string Password { get; set; }
            public string role { get; set; }
        }
        // ✅ Get all department IDs from admin table
        [HttpGet("departments")]
        public IActionResult GetAllDepartments()
        {
            try
            {
                var connectionString = _configuration.GetConnectionString("DefaultConnection");
                using var conn = new NpgsqlConnection(connectionString);
                conn.Open();

                var query = "SELECT department_id FROM admin WHERE department_id != 'ADMIN' ORDER BY department_id";

                using var cmd = new NpgsqlCommand(query, conn);
                using var reader = cmd.ExecuteReader();

                var departments = new List<string>();
                while (reader.Read())
                {
                    departments.Add(reader.GetString(0));
                }

                return Ok(departments);
            }
            catch (Exception ex)
            {
                return StatusCode(500, new { message = "Failed to load departments", error = ex.Message });
            }
        }
    }

    // DTOs
    public class LoginDto
    {
        public string Username { get; set; }
        public string Password { get; set; }
    }


}
