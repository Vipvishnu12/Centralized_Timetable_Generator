using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Configuration;
using Npgsql;
using System.Collections.Generic;
using System.Data;
using System.Runtime.Intrinsics.X86;

namespace YourNamespace.Controllers
{
    [ApiController]
    [Route("api/[controller]")]
    public class LabController : ControllerBase
    {
        private readonly IConfiguration _configuration;

        public LabController(IConfiguration configuration)
        {
            _configuration = configuration;
        }

        [HttpPost("create")]
        public async Task<IActionResult> CreateLab([FromBody] LabModel lab)
        {
            if (lab == null || string.IsNullOrWhiteSpace(lab.LabId) ||
                string.IsNullOrWhiteSpace(lab.LabName) ||
                string.IsNullOrWhiteSpace(lab.Department) ||
                lab.Systems <= 0)
            {
                return BadRequest(new { message = "⚠️ Invalid lab data." });
            }

            try
            {
                var connectionString = _configuration.GetConnectionString("DefaultConnection");

                using var conn = new NpgsqlConnection(connectionString);
                await conn.OpenAsync();


                var query = @"
            INSERT INTO labdata (lab_id, lab_name, departmentid, lab_capacity,block)
            VALUES (@labId, @labName, @department, @systems,@block)
        ";

                using var cmd = new NpgsqlCommand(query, conn);
                cmd.Parameters.AddWithValue("@labId", lab.LabId);
                cmd.Parameters.AddWithValue("@labName", lab.LabName);
                cmd.Parameters.AddWithValue("@department", lab.Department);
                cmd.Parameters.AddWithValue("@systems", lab.Systems);
                cmd.Parameters.AddWithValue("@block", lab.Block);
                await cmd.ExecuteNonQueryAsync();

                return Ok(new { message = "✅ Lab created successfully." });
            }
            catch (PostgresException ex) when (ex.SqlState == "23505") // Unique violation
            {
                return Conflict(new { message = "❌ Lab ID already exists." });
            }
            catch (Exception ex)
            {
                return StatusCode(500, new { message = "❌ Internal Server Error", error = ex.Message });
            }
        }

        [HttpPut("update")]
        public async Task<IActionResult> UpdateLab([FromBody] LabModel lab)
        {
            if (lab == null || string.IsNullOrWhiteSpace(lab.LabId))
            {
                return BadRequest(new { message = "⚠️ Invalid lab data." });
            }

            try
            {
                var connectionString = _configuration.GetConnectionString("DefaultConnection");

                using var conn = new NpgsqlConnection(connectionString);
                await conn.OpenAsync();

                var query = @"
    UPDATE labdata
    SET lab_name = @labName,
        departmentid = @department,
        lab_capacity = @systems
    WHERE lab_id = @labId
";

                using var cmd = new NpgsqlCommand(query, conn);
                cmd.Parameters.AddWithValue("@labId", lab.LabId);
                cmd.Parameters.AddWithValue("@labName", lab.LabName);
                cmd.Parameters.AddWithValue("@department", lab.Department);
                cmd.Parameters.AddWithValue("@systems", lab.Systems);

                var rows = await cmd.ExecuteNonQueryAsync();

                if (rows > 0)
                    return Ok(new { message = "✅ Lab updated successfully." });
                else
                    return NotFound(new { message = "❌ Lab not found." });
            }
            catch (Exception ex)
            {
                return StatusCode(500, new { message = "❌ Update failed.", error = ex.Message });
            }
        }






        [HttpGet("all")]
        public async Task<IActionResult> GetAllLabs([FromQuery] string departmentId)
        {
            try
            {
                var connectionString = _configuration.GetConnectionString("DefaultConnection");

                using var conn = new NpgsqlConnection(connectionString);
                await conn.OpenAsync();

                // Modify the query to filter by departmentId
                var query = "SELECT lab_id, lab_name, departmentid, lab_capacity FROM labdata WHERE departmentid = @DepartmentId";

                using var cmd = new NpgsqlCommand(query, conn);
                cmd.Parameters.AddWithValue("@DepartmentId", departmentId);  // Pass departmentId as a parameter
                using var reader = await cmd.ExecuteReaderAsync();

                var labs = new List<object>();

                while (await reader.ReadAsync())
                {
                    labs.Add(new
                    {
                        LabId = reader["lab_id"].ToString(),
                        LabName = reader["lab_name"].ToString(),
                        Department = reader["departmentid"].ToString(),
                        Systems = Convert.ToInt32(reader["lab_capacity"])
                    });
                }

                return Ok(labs);
            }
            catch (Exception ex)
            {
                return StatusCode(500, new { message = "❌ Failed to retrieve labs.", error = ex.Message });
            }
        }


        [HttpGet("getTimetableByLabId")]
        public async Task<IActionResult> GetTimetableByLabId([FromQuery] string labId)
        {
            if (string.IsNullOrWhiteSpace(labId))
                return BadRequest(new { message = "⚠️ Lab ID is required." });

            try
            {
                var connectionString = _configuration.GetConnectionString("DefaultConnection");

                using var conn = new NpgsqlConnection(connectionString);
                await conn.OpenAsync();

                var query = @"
            SELECT subject_code, subject_name, staff_name, department, year, semester, section, day, hour
            FROM labtimetable
            WHERE lab_id = @labId
            ORDER BY day, hour";

                using var cmd = new NpgsqlCommand(query, conn);
                cmd.Parameters.AddWithValue("@labId", labId);

                using var reader = await cmd.ExecuteReaderAsync();

                var records = new List<object>();

                while (await reader.ReadAsync())
                {
                    records.Add(new
                    {
                        SubjectId = reader["subject_name"].ToString(),
                        SubjectCode = reader["subject_code"].ToString(),
                        Staff = reader["staff_name"].ToString(),
                        Department = reader["department"].ToString(),
                        Year = reader["year"].ToString(),
                        Semester = reader["semester"].ToString(),
                        Section = reader["section"].ToString(),
                        Day = reader["day"].ToString(),
                        Hour = Convert.ToInt32(reader["hour"])
                    });
                }

                return Ok(new
                {
                    labId = labId,
                    labName = labId, // You can replace this if you fetch from `labs` table
                    records
                });
            }
            catch (Exception ex)
            {
                return StatusCode(500, new { message = "❌ Failed to load timetable.", error = ex.Message });
            }
        }
        [HttpGet("all1")]
        public async Task<IActionResult> GetAllLabs()
        {
            try
            {
                var connectionString = _configuration.GetConnectionString("DefaultConnection");

                using var conn = new NpgsqlConnection(connectionString);
                await conn.OpenAsync();

                var query = "SELECT lab_id, lab_name, departmentid, lab_capacity FROM labdata";
                using var cmd = new NpgsqlCommand(query, conn);
                using var reader = await cmd.ExecuteReaderAsync();

                var labs = new List<object>();

                while (await reader.ReadAsync())
                {
                    labs.Add(new
                    {
                        LabId = reader["lab_id"].ToString(),
                        LabName = reader["lab_name"].ToString(),
                        Department = reader["departmentid"].ToString(),
                        Systems = Convert.ToInt32(reader["lab_capacity"])
                    });
                }

                return Ok(labs);
            }
            catch (Exception ex)
            {
                return StatusCode(500, new { message = "❌ Failed to retrieve labs.", error = ex.Message });
            }
        }


        [HttpGet("pendingLabRequestsCount")]
        public async Task<IActionResult> GetPendingLabRequestsCount()
        {
            var connectionString = _configuration.GetConnectionString("DefaultConnection");

            try
            {
                using var conn = new NpgsqlConnection(connectionString);
                await conn.OpenAsync();

                var countCmd = new NpgsqlCommand(
                    "SELECT COUNT(*) FROM pendingtimetabledata WHERE lab_id = @labId", conn);
                countCmd.Parameters.AddWithValue("@labId", "From Other Department");

                var count = Convert.ToInt32(await countCmd.ExecuteScalarAsync());

                return Ok(new { pendingLabRequestsCount = count });
            }
            catch (Exception ex)
            {
                return StatusCode(500, new { message = "Internal Server Error", error = ex.Message });
            }
        }

        [HttpGet("received")]
    public async Task<IActionResult> GetAllLabreq()
    {
        try
        {
            var connectionString = _configuration.GetConnectionString("DefaultConnection");

            using var conn = new NpgsqlConnection(connectionString);
            await conn.OpenAsync();

            var query = @"SELECT subject_id, lab_id, subject_shrt, credit, subtype,
       department, year, sem, lab_department, section
FROM pendingtimetabledata 
WHERE lab_id = 'From Other Department'
";
            using var cmd = new NpgsqlCommand(query, conn);
            using var reader = await cmd.ExecuteReaderAsync();

            var pendingRequests = new List<PendingLabRequestDto>();

            while (await reader.ReadAsync())
            {
                pendingRequests.Add(new PendingLabRequestDto
                {
                    SubjectId = reader["subject_id"].ToString(),
                    LabId = reader["lab_id"].ToString(),
                    SubjectShrt = reader["subject_shrt"].ToString(),
                    Credit = Convert.ToInt32(reader["credit"]),
                    Subtype = reader["subtype"].ToString(),
                    Department = reader["department"].ToString(),
                    Year = reader["year"].ToString(),
                    Sem = reader["sem"].ToString(),
                    LabDepartment = reader["lab_department"].ToString(),
                    Section = reader["section"].ToString()
                });
            }

            return Ok(pendingRequests);
        }
        catch (Exception ex)
        {
            return StatusCode(500, new { message = "❌ Failed to retrieve data.", error = ex.Message });
        }
    }


        [HttpPost("updateAssignedLabs")]
        public async Task<IActionResult> UpdateAssignedLabs([FromBody] List<AssignedLabDto> labAssignments)
        {
            try
            {
                var connectionString = _configuration.GetConnectionString("DefaultConnection");

                using var conn = new NpgsqlConnection(connectionString);
                await conn.OpenAsync();

                foreach (var assignment in labAssignments)
                {
                    var query = @"UPDATE pendingtimetabledata
                          SET lab_id = @LabId
                          WHERE subject_id = @SubjectId AND
                                section = @Section AND
                                year = @Year AND
                                sem = @Semester AND
                                department = @LabDepartment";

                    using var cmd = new NpgsqlCommand(query, conn);
                    cmd.Parameters.AddWithValue("@LabId", assignment.AssignedLab);
                    cmd.Parameters.AddWithValue("@SubjectId", assignment.SubjectId);
                    cmd.Parameters.AddWithValue("@Section", assignment.Section);
                    cmd.Parameters.AddWithValue("@Year", assignment.Year);
                    cmd.Parameters.AddWithValue("@Semester", assignment.Semester);
                    cmd.Parameters.AddWithValue("@LabDepartment", assignment.LabDepartment);

                    await cmd.ExecuteNonQueryAsync();
                }

                return Ok(new { message = "✅ Lab assignments updated successfully!" });
            }
            catch (Exception ex)
            {
                return StatusCode(500, new { message = "❌ Failed to update lab assignments", error = ex.Message });
            }
        }


        public class AssignedLabDto
        {
            public string SubjectId { get; set; }
            public string AssignedLab { get; set; }
            public string Section { get; set; }
            public string Year { get; set; }
            public string Semester { get; set; }
            public string LabDepartment { get; set; }
        }


        public class PendingLabRequestDto
        {
            public string SubjectId { get; set; }
            public string LabId { get; set; }
            public string SubjectShrt { get; set; }
            public int Credit { get; set; }
            public string Subtype { get; set; }
            public string Department { get; set; }
            public string Year { get; set; }
            public string Sem { get; set; }
            public string LabDepartment { get; set; }
            public string Section { get; set; }
        }




        public class LabTimetableResponseDto
        {
            public string LabId { get; set; }
            public string LabName { get; set; }
            public List<LabTimetableRecordDto> Records { get; set; }
        }

        public class LabTimetableRecordDto
        {
            public string SubjectId { get; set; }
            public string SubjectCode { get; set; }
            public string Staff { get; set; }
            public string Department { get; set; }
            public string Year { get; set; }
            public string Semester { get; set; }
            public string Section { get; set; }
            public string Day { get; set; }
            public int Hour { get; set; }
        }

        public class LabModel
        {
            public string LabId { get; set; }
            public string LabName { get; set; }
            public string Department { get; set; }
            public string Block { get; set; }
            public int Systems { get; set; }
        }
        // ✅ DTOs
    }
}