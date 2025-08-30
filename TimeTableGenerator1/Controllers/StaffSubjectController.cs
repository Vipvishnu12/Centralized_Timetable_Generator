using Microsoft.AspNetCore.Mvc;
using Npgsql;
using Timetablegenerator.Connection;
using System.Collections.Generic;

namespace Timetablegenerator.Controllers
{
    [ApiController]
    [Route("api/[controller]")]
    public class StaffSubjectController : ControllerBase
    {
        private readonly DatabaseConnection _db;

        public StaffSubjectController(DatabaseConnection db)
        {
            _db = db;
        }

        // ✅ 1. Get subjects by year, sem, and department
        [HttpGet("subjects")]
        public IActionResult GetSubjects([FromQuery] string year, [FromQuery] string sem, [FromQuery] string departmentId)
        {
            if (string.IsNullOrWhiteSpace(year) || string.IsNullOrWhiteSpace(sem) || string.IsNullOrWhiteSpace(departmentId))
                return BadRequest(new { message = "Year, Semester, and Department ID are required." });

            try
            {


                using var conn = _db.GetConnection();
                conn.Open();

                string query = @"
                    SELECT subject_shortform, subject_code, subject_name, subject_type, credit
                    FROM subject_data
                    WHERE year = @year AND sem = @sem AND department_id = @department_id;
                ";

                using var cmd = new NpgsqlCommand(query, conn);
                cmd.Parameters.AddWithValue("@year", year);
                cmd.Parameters.AddWithValue("@sem", sem);
                cmd.Parameters.AddWithValue("@department_id", departmentId);

                var result = new List<SubjectResultDto>();
                using var reader = cmd.ExecuteReader();
                while (reader.Read())
                {
                    result.Add(new SubjectResultDto
                    {
                        SubjectId = reader["subject_shortform"].ToString(),
                        SubCode = reader["subject_code"].ToString(),
                        SubjectName = reader["subject_name"].ToString(),
                        SubjectType = reader["subject_type"].ToString(),
                        Credit = reader["credit"] != DBNull.Value ? Convert.ToInt32(reader["credit"]) : 0
                    });
                }

                return Ok(result);
            }
            catch (PostgresException pgEx)
            {
                return StatusCode(500, new { message = "Database error", error = pgEx.Message });
            }
            catch (System.Exception ex)
            {
                return StatusCode(500, new { message = "Server error", error = ex.Message });
            }
        }

        // ✅ 2. Get staff list by department
        [HttpGet("staff")]
        public IActionResult GetStaffByDepartment([FromQuery] string departmentId)
        {
            if (string.IsNullOrWhiteSpace(departmentId))
                return BadRequest(new { message = "Department ID is required." });

            try
            {
                using var conn = _db.GetConnection();
                conn.Open();

                string query = @"
                    SELECT staffid, staffname
                    FROM staff_data
                    WHERE departmentid = @department_id;
                ";

                using var cmd = new NpgsqlCommand(query, conn);
                cmd.Parameters.AddWithValue("@department_id", departmentId);

                var result = new List<StaffResultDto>();
                using var reader = cmd.ExecuteReader();
                while (reader.Read())
                {
                    result.Add(new StaffResultDto
                    {
                        StaffId = reader["staffid"].ToString(),
                        StaffName = reader["staffname"].ToString()
                    });
                }

                return Ok(result);
            }
            catch (PostgresException pgEx)
            {
                return StatusCode(500, new { message = "Database error", error = pgEx.Message });
            }
            catch (System.Exception ex)
            {
                return StatusCode(500, new { message = "Server error", error = ex.Message });
            }
        }

        // ✅ DTOs
        public class SubjectResultDto
        {
            public string SubjectId { get; set; }        // ✅ New field
            public string SubCode { get; set; }
            public string SubjectName { get; set; }
            public string SubjectType { get; set; }
            public int Credit { get; set; }
        }

        public class StaffResultDto
        {
            public string StaffId { get; set; }
            public string StaffName { get; set; }
        }
    }
}
