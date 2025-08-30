using Microsoft.AspNetCore.Mvc;
using Npgsql;
using Timetablegenerator.Connection;
using System.Collections.Generic;

namespace Timetablegenerator.Controllers
{
    [ApiController]
    [Route("api/[controller]")]
    public class SubjectDataController : ControllerBase
    {
        private readonly DatabaseConnection _db;

        public SubjectDataController(DatabaseConnection db)
        {
            _db = db;
        }

        // ✅ POST: api/SubjectData/add
        [HttpPost("add")]
        public IActionResult AddSubject([FromBody] SubjectDataDto dto)
        {
            if (dto == null)
                return BadRequest(new { message = "Invalid subject data." });

            using var conn = _db.GetConnection();
            conn.Open();

            try
            {



                string query = @"
                    INSERT INTO subject_data
                    (subject_shortform, subject_code, subject_name, year, sem, department_id, subject_type, credit)
                    VALUES 
                    (@subject_id, @sub_code, @subject_name, @year, @sem, @department_id, @subject_type, @credit);
                ";

                using var cmd = new NpgsqlCommand(query, conn);
                cmd.Parameters.AddWithValue("@subject_id", dto.Subject_Id ?? "");
                cmd.Parameters.AddWithValue("@sub_code", dto.Sub_Code ?? "");
                cmd.Parameters.AddWithValue("@subject_name", dto.Subject_Name ?? "");
                cmd.Parameters.AddWithValue("@year", dto.Year ?? "");
                cmd.Parameters.AddWithValue("@sem", dto.Sem ?? "");
                cmd.Parameters.AddWithValue("@department_id", dto.Department_Id ?? "");
                cmd.Parameters.AddWithValue("@subject_type", dto.Subject_Type ?? "");
                cmd.Parameters.AddWithValue("@credit", dto.Credit);

                cmd.ExecuteNonQuery();
                return Ok(new { message = "✅ Subject data inserted successfully." });
            }
            catch (PostgresException ex)
            {
                return StatusCode(500, new { message = "❌ PostgreSQL error", error = ex.Message });
            }
            catch (Exception ex)
            {
                return StatusCode(500, new { message = "❌ Server error", error = ex.Message });
            }
        }

        // ✅ GET: api/SubjectData/view/{year}/{sem}/{departmentId}
        [HttpGet("view/{year}/{sem}/{departmentId}")]
        public IActionResult ViewSubjects(string year, string sem, string departmentId)
        {
            using var conn = _db.GetConnection();
            conn.Open();

            try
            {
                string query = @"
                    SELECT subject_shortform, subject_code, subject_name, year, sem, department_id, subject_type, credit
                    FROM subject_data
                    WHERE year = @year AND sem = @sem AND department_id = @department_id;
                ";

                using var cmd = new NpgsqlCommand(query, conn);
                cmd.Parameters.AddWithValue("@year", year);
                cmd.Parameters.AddWithValue("@sem", sem);
                cmd.Parameters.AddWithValue("@department_id", departmentId);

                var reader = cmd.ExecuteReader();
                var subjects = new List<SubjectDataDto>();

                while (reader.Read())
                {
                    subjects.Add(new SubjectDataDto
                    {
                        Subject_Id = reader["subject_shortform"].ToString(),
                        Sub_Code = reader["subject_code"].ToString(),
                        Subject_Name = reader["subject_name"].ToString(),
                        Year = reader["year"].ToString(),
                        Sem = reader["sem"].ToString(),
                        // Department = reader["department"].ToString(),
                        Department = reader["department_id"].ToString(),
                        Subject_Type = reader["subject_type"].ToString(),
                        Credit = reader["credit"] != DBNull.Value ? Convert.ToInt32(reader["credit"]) : 0
                    });
                }

                return Ok(subjects);
            }
            catch (PostgresException ex)
            {
                return StatusCode(500, new { message = "❌ PostgreSQL error", error = ex.Message });
            }
            catch (Exception ex)
            {
                return StatusCode(500, new { message = "❌ Server error", error = ex.Message });
            }
        }




        // ✅ PUT: api/SubjectData/update
        [HttpPut("update")]
        public IActionResult UpdateSubject([FromBody] SubjectDataDto subject)
        {
            if (subject == null || string.IsNullOrEmpty(subject.Sub_Code))
                return BadRequest(new { message = "❌ Invalid subject data" });

            using var conn = _db.GetConnection();
            conn.Open();

            try
            {
                string query = @"
                    UPDATE subject_data SET
                        subject_name = @name,
                        subject_type = @type,
                        credit = @credit
                    WHERE subject_code = @code AND year = @year AND sem = @sem AND department_id = @department_id;
                ";

                using var cmd = new NpgsqlCommand(query, conn);
                cmd.Parameters.AddWithValue("@name", subject.Subject_Name ?? "");
                cmd.Parameters.AddWithValue("@type", subject.Subject_Type ?? "");
                cmd.Parameters.AddWithValue("@credit", subject.Credit);
                cmd.Parameters.AddWithValue("@code", subject.Sub_Code ?? "");
                cmd.Parameters.AddWithValue("@year", subject.Year ?? "");
                cmd.Parameters.AddWithValue("@sem", subject.Sem ?? "");
                cmd.Parameters.AddWithValue("@department_id", subject.Department_Id ?? "");

                int affected = cmd.ExecuteNonQuery();

                return Ok(new
                {
                    message = affected > 0 ? "✅ Subject updated successfully" : "❌ Subject not found"
                });
            }
            catch (PostgresException ex)
            {
                return StatusCode(500, new { message = "❌ PostgreSQL error", error = ex.Message });
            }
            catch (Exception ex)
            {
                return StatusCode(500, new { message = "❌ Server error", error = ex.Message });
            }
        }
    }

    // ✅ DTO
    public class SubjectDataDto
    {
        public string Subject_Id { get; set; }
        public string Sub_Code { get; set; }
        public string Subject_Name { get; set; }
        public string Year { get; set; }
        public string Sem { get; set; }
        public string Department { get; set; }
        public string Department_Id { get; set; }
        public string Subject_Type { get; set; }
        public int Credit { get; set; }
    }
}
