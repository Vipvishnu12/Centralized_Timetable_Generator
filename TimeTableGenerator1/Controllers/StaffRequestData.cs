using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Configuration;
using Npgsql;
using System;
using System.Collections.Generic;
using System.Data;
using System.Threading.Tasks;

namespace Timetablegenerator.Controllers
{
    [ApiController]
    [Route("api/[controller]")]
    public class StaffRequestData : ControllerBase
    {
        private readonly IConfiguration _configuration;

        public StaffRequestData(IConfiguration configuration)
        {
            _configuration = configuration;
        }

        // ✅ 1. POST: Assign from Other Department
        [HttpPost("assignFromOtherDept")]
        public IActionResult AssignFromOtherDepartment([FromBody] AssignOtherDeptRequest req)
        {
            try
            {

                var connectionString = _configuration.GetConnectionString("DefaultConnection");

                using var conn = new NpgsqlConnection(connectionString);
                conn.Open();

                var query = @"
                    INSERT INTO cross_department_assignments 
                    (from_department, to_department, subject_code, subject_name, year, semester, section,lab_id)
                    VALUES (@fromDept, @toDept, @subCode, @subName, @year, @semester, @section,@labId);
                ";

                using var cmd = new NpgsqlCommand(query, conn);
                cmd.Parameters.AddWithValue("@fromDept", req.FromDepartment);
                cmd.Parameters.AddWithValue("@toDept", req.ToDepartment);
                cmd.Parameters.AddWithValue("@subCode", req.SubjectCode);
                cmd.Parameters.AddWithValue("@subName", req.SubjectName);
                cmd.Parameters.AddWithValue("@year", req.Year);
                cmd.Parameters.AddWithValue("@semester", req.Semester);
                cmd.Parameters.AddWithValue("@section", req.Section);
                cmd.Parameters.AddWithValue("@labId", req.labId);


                int affectedRows = cmd.ExecuteNonQuery();

                return Ok(new { message = "Assignment saved successfully", rowsAffected = affectedRows });
            }
            catch (Exception ex)
            {
                return StatusCode(500, new { message = "Server error", error = ex.Message });
            }
        }

        // ✅ 2. GET: Received assignments for logged department
        [HttpGet("received")]
        public async Task<IActionResult> GetReceivedAssignments([FromQuery] string department)
        {
            var results = new List<dynamic>();
            var connectionString = _configuration.GetConnectionString("DefaultConnection");

            try
            {
                await using var conn = new NpgsqlConnection(connectionString);
                await conn.OpenAsync();

                var sql = @"
                    SELECT 
                        subject_code AS subCode,
                        subject_name AS subjectName,
                        'Core' AS subjectType,
                        4 AS credit, -- adjust as needed
                        from_department AS department,
                        year,
                        semester,
                        section,
                        assigned_staff
                    FROM cross_department_assignments
                    WHERE to_department = @department";

                await using var cmd = new NpgsqlCommand(sql, conn);
                cmd.Parameters.AddWithValue("department", department);

                await using var reader = await cmd.ExecuteReaderAsync();
                while (await reader.ReadAsync())
                {
                    results.Add(new
                    {
                        subCode = reader["subCode"].ToString(),
                        subjectName = reader["subjectName"].ToString(),
                        subjectType = reader["subjectType"].ToString(),
                        credit = Convert.ToInt32(reader["credit"]),
                        department = reader["department"].ToString(),
                        year = reader["year"].ToString(),
                        semester = reader["semester"].ToString(),
                        section = reader["section"].ToString(),
                        staff_assigned = reader["assigned_staff"] == DBNull.Value ? null : reader["assigned_staff"].ToString()
                    });
                }

                return Ok(results);
            }
            catch (Exception ex)
            {
                return StatusCode(500, new { message = "Failed to load received assignments", error = ex.Message });
            }
        }

        // ✅ 3. GET: Staff list by department
        [HttpGet("staff")]
        public async Task<IActionResult> GetStaffByDepartment([FromQuery] string department)
        {
            var results = new List<dynamic>();
            var connectionString = _configuration.GetConnectionString("DefaultConnection");

            try
            {
                await using var conn = new NpgsqlConnection(connectionString);
                await conn.OpenAsync();

                var sql = "SELECT staff_id, staff_name FROM staff_master WHERE department = @department";

                await using var cmd = new NpgsqlCommand(sql, conn);
                cmd.Parameters.AddWithValue("department", department);

                await using var reader = await cmd.ExecuteReaderAsync();
                while (await reader.ReadAsync())
                {
                    results.Add(new
                    {
                        staffId = reader["staff_id"].ToString(),
                        staffName = reader["staff_name"].ToString()
                    });
                }

                return Ok(results);
            }
            catch (Exception ex)
            {
                return StatusCode(500, new { message = "Failed to fetch staff", error = ex.Message });
            }
        }

        // ✅ 4. POST: Assign staff for a received subject
        [HttpPost("assignStaff")]
        public async Task<IActionResult> AssignStaff([FromBody] AssignStaffRequest request)
        {
            var connectionString = _configuration.GetConnectionString("DefaultConnection");

            try
            {
                await using var conn = new NpgsqlConnection(connectionString);
                await conn.OpenAsync();

                var sql = @"
                    UPDATE cross_department_assignments
                    SET assigned_staff = @staff_assigned
                    WHERE subject_code = @subCode
                      AND to_department = @toDepartment
                      AND year = @year
                      AND semester = @semester
                      AND section = @section";

                await using var cmd = new NpgsqlCommand(sql, conn);
                cmd.Parameters.AddWithValue("staff_assigned", request.StaffAssigned);
                cmd.Parameters.AddWithValue("subCode", request.SubCode);
                cmd.Parameters.AddWithValue("toDepartment", request.ToDepartment);
                cmd.Parameters.AddWithValue("year", request.Year);
                cmd.Parameters.AddWithValue("semester", request.Semester);
                cmd.Parameters.AddWithValue("section", request.Section);

                int rows = await cmd.ExecuteNonQueryAsync();
                return Ok(new { message = "Staff assigned successfully", rowsAffected = rows });
            }
            catch (Exception ex)
            {
                return StatusCode(500, new { message = "Assignment failed", error = ex.Message });
            }
        }


        [HttpPost("updateAssignedStaff")]
        public async Task<IActionResult> UpdateAssignedStaff([FromBody] List<AssignedStaffUpdateRequest> requests)
        {
            var connectionString = _configuration.GetConnectionString("DefaultConnection");

            try
            {
                using var conn = new NpgsqlConnection(connectionString);
                await conn.OpenAsync();

                foreach (var req in requests)
                {

                    // Update cross_department_assignments
                    var updateCrossQuery = @"
                UPDATE pendingtimetabledata
                SET staffname = @staff
                WHERE subject_id = @subCode 
                  AND subject_shrt = @subName 
                  AND year = @year 
                  AND sem = @semester 
                  AND section = @section
            ";

                    using (var cmd1 = new NpgsqlCommand(updateCrossQuery, conn))
                    {
                        cmd1.Parameters.AddWithValue("@staff", req.assignedStaff ?? "");
                        cmd1.Parameters.AddWithValue("@subCode", req.subCode);
                        cmd1.Parameters.AddWithValue("@subName", req.subjectName);
                        cmd1.Parameters.AddWithValue("@year", req.year);
                        cmd1.Parameters.AddWithValue("@semester", req.semester);
                        cmd1.Parameters.AddWithValue("@section", req.section);
                        await cmd1.ExecuteNonQueryAsync();
                    }

                }

                return Ok(new { message = "Staff assignments updated and removed from cross_department_assignments." });
            }
            catch (Exception ex)
            {
                return StatusCode(500, new { message = "Error during update/delete operation", error = ex.Message });
            }
        }





    }

    public class AssignOtherDeptRequest
    {
        public string FromDepartment { get; set; }
        public string ToDepartment { get; set; }
        public string SubjectCode { get; set; }
        public string SubjectName { get; set; }
        public string Year { get; set; }
        public string Semester { get; set; }
        public string Section { get; set; }
        public string labId { get; set; }
    }

    public class AssignStaffRequest
    {
        public string SubCode { get; set; }
        public string ToDepartment { get; set; }
        public string Year { get; set; }
        public string Semester { get; set; }
        public string Section { get; set; }
        public string StaffAssigned { get; set; }
    }
    public class AssignedStaffUpdateRequest
    {
        public string subCode { get; set; }
        public string subjectName { get; set; }
        public string year { get; set; }
        public string semester { get; set; }
        public string section { get; set; }
        public string assignedStaff { get; set; }
    }
}
