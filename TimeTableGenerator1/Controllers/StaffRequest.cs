using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Configuration;
using Npgsql;
using System;
using System.Collections.Generic;

namespace YourNamespace.Controllers
{
    [ApiController]
    [Route("api/[controller]")]
    public class StaffRequestDataController : ControllerBase
    {
        private readonly IConfiguration _configuration;

        public StaffRequestDataController(IConfiguration configuration)
        {
            _configuration = configuration;
        }

        private NpgsqlConnection GetConnection()
        {
            return new NpgsqlConnection(_configuration.GetConnectionString("DefaultConnection"));
        }

        // ✅ Get all pending requests (Assigned_Staff is NULL)
        [HttpGet("pending")]
        public IActionResult GetPendingRequests()
        {
            var results = new List<object>();

            try
            {
                using var conn = GetConnection();
                conn.Open();

                var query = @"
                    SELECT id, from_department, to_department, subject_code, subject_name,
                           year, semester, section
                    FROM cross_department_assignments
                    WHERE assigned_staff IS NULL
                    ORDER BY year, semester, section;
                ";

                using var cmd = new NpgsqlCommand(query, conn);
                using var reader = cmd.ExecuteReader();
                while (reader.Read())
                {
                    results.Add(new
                    {
                        Id = reader.GetInt32(0),
                        FromDepartment = reader.GetString(1),
                        ToDepartment = reader.GetString(2),
                        SubjectCode = reader.GetString(3),
                        SubjectName = reader.GetString(4),
                        Year = reader.GetString(5),
                        Semester = reader.GetString(6),
                        Section = reader.GetString(7),
                        Status = "Pending"
                    });
                }

                return Ok(results);
            }
            catch (Exception ex)
            {
                return StatusCode(500, new { message = "Error fetching requests", error = ex.Message });
            }
        }

        // ✅ Approve a single request by id
        [HttpPost("approve")]
        public IActionResult ApproveRequest([FromBody] IdDto dto)
        {
            try
            {
                using var conn = GetConnection();
                conn.Open();

                var query = @"
                    UPDATE cross_department_assignments
                    SET assigned_staff = 'Approved'
                    WHERE id = @id
                ";

                using var cmd = new NpgsqlCommand(query, conn);
                cmd.Parameters.AddWithValue("@id", dto.Id);
                int affected = cmd.ExecuteNonQuery();

                if (affected > 0)
                    return Ok(new { message = "Request approved" });
                else
                    return NotFound(new { message = "Request not found" });
            }
            catch (Exception ex)
            {
                return StatusCode(500, new { message = "Approval failed", error = ex.Message });
            }
        }

        // ❌ Reject request by deleting (or mark rejected)
        [HttpPost("reject")]
        public IActionResult RejectRequest([FromBody] IdDto dto)
        {
            try
            {
                using var conn = GetConnection();
                conn.Open();

                var query = @"DELETE FROM subject_assignments WHERE id = @id";

                using var cmd = new NpgsqlCommand(query, conn);
                cmd.Parameters.AddWithValue("@id", dto.Id);
                int affected = cmd.ExecuteNonQuery();

                if (affected > 0)
                    return Ok(new { message = "Request rejected" });
                else
                    return NotFound(new { message = "Request not found" });
            }
            catch (Exception ex)
            {
                return StatusCode(500, new { message = "Rejection failed", error = ex.Message });
            }
        }

        // ✅ Bulk approval for same year, sem, section, department
        [HttpPost("approveAll")]
        public IActionResult ApproveAll([FromBody] BulkApprovalDto dto)
        {
            try
            {
                using var conn = GetConnection();
                conn.Open();

                var query = @"
                    UPDATE cross_department_assignments
                    SET assigned_staff = 'Approved'
                    WHERE year = @year AND semester = @semester AND section = @section AND department = @department
                          AND assigned_staff IS NULL
                ";

                using var cmd = new NpgsqlCommand(query, conn);
                cmd.Parameters.AddWithValue("@year", dto.Year);
                cmd.Parameters.AddWithValue("@semester", dto.Semester);
                cmd.Parameters.AddWithValue("@section", dto.Section);
                cmd.Parameters.AddWithValue("@department", dto.Department);

                int affected = cmd.ExecuteNonQuery();
                return Ok(new { message = $"Approved {affected} records" });
            }
            catch (Exception ex)
            {
                return StatusCode(500, new { message = "Bulk approval failed", error = ex.Message });
            }
        }

        public class IdDto
        {
            public int Id { get; set; }
        }

        public class BulkApprovalDto
        {
            public string Year { get; set; }
            public string Semester { get; set; }
            public string Section { get; set; }
            public string Department { get; set; }
        }
    }
}
