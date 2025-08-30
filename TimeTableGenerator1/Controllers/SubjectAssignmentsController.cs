using Azure.Core;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Configuration;
using Npgsql;
using System;
using System.Collections.Generic;
using System.Data;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Text.Json.Serialization;
using System.Threading.Tasks;
using TimetableGA;
using Timetablegenerator;
using static System.Collections.Specialized.BitVector32;
using static TimetableGA.TimetableEngine;

namespace YourNamespace.Controllers
{
    [ApiController]
    [Route("api/[controller]")]
    public class CrossDepartmentAssignmentsController : ControllerBase
    {
        private readonly IConfiguration _configuration;

        public CrossDepartmentAssignmentsController(IConfiguration configuration)
        {
            _configuration = configuration;
        }

        [HttpGet("grouped")]
        public async Task<IActionResult> GetGroupedAssignments([FromQuery] string department)
        {
            var connectionString = _configuration.GetConnectionString("DefaultConnection");

            try
            {
                var results = new List<dynamic>();
                using var conn = new NpgsqlConnection(connectionString);
                await conn.OpenAsync();



                var sql = @"
                    SELECT 
                        id,
                        
                        staffname,
                        department,
                    
                        subject_id,
                        subject_shrt,
                        year,
                        sem,
                        section
                       ,lab_id
                        
                    FROM pendingtimetabledata
                    WHERE department = @department and status!='generated'
                    ORDER BY year, sem, section, department
                ";

                using var cmd = new NpgsqlCommand(sql, conn);
                cmd.Parameters.AddWithValue("@department", department);

                using var reader = await cmd.ExecuteReaderAsync();
                while (await reader.ReadAsync())
                {
                    results.Add(new
                    {
                        id = Convert.ToInt32(reader["id"]),
                        department = reader["department"].ToString(),
                        subject_code = reader["subject_id"].ToString(),
                        subject_name = reader["subject_shrt"].ToString(),
                        year = reader["year"].ToString(),
                        semester = reader["sem"].ToString(),
                        section = reader["section"].ToString(),
                        assignedStaff = reader["staffname"] == DBNull.Value ? null : reader["staffname"].ToString(),
                        labid = reader["lab_id"] == DBNull.Value ? null : reader["lab_id"].ToString()
                    });
                }

                return Ok(results);
            }
            catch (Exception ex)
            {
                return StatusCode(500, new { message = "Failed to retrieve data", error = ex.Message });
            }
        }

 
        // ✅ UPDATED BACKEND CONTROLLER METHOD
        [HttpGet("getCrossTimetable")]
        public async Task<IActionResult> GetCrossTimetable(
       [FromQuery] string toDepartment,
       [FromQuery] string year,
       [FromQuery] string semester,
       [FromQuery] string section)
        {
            var connectionString = _configuration.GetConnectionString("DefaultConnection");

            try
            {
                var result = new Dictionary<string, Dictionary<int, (string Display, string SubjectId)>>();

                using var conn = new NpgsqlConnection(connectionString);
                await conn.OpenAsync();

                var query = @"
            SELECT 
                cct.day, 
                cct.hour, 
                cct.subject_code, 
                cct.staff_name, 
                sd.subject_shortform
            FROM classtimetable cct
            LEFT JOIN subject_data sd ON cct.subject_code = sd.subject_code
            WHERE cct.department_id = @department_id
              AND cct.year = @year
              AND cct.semester = @semester
              AND cct.section = @section
        ";

                using var cmd = new NpgsqlCommand(query, conn);
                cmd.Parameters.AddWithValue("@department_id", toDepartment.Trim());
                cmd.Parameters.AddWithValue("@year", year.Trim());
                cmd.Parameters.AddWithValue("@semester", semester.Trim());
                cmd.Parameters.AddWithValue("@section", section.Trim());

                using var reader = await cmd.ExecuteReaderAsync();
                while (await reader.ReadAsync())
                {
                    var day = reader["day"].ToString();
                    var hour = Convert.ToInt32(reader["hour"]);
                    var subjectCode = reader["subject_code"].ToString();
                    var staff = reader["staff_name"].ToString();
                    var subjectId = reader["subject_shortform"]?.ToString() ?? "0"; // VARCHAR support

                    var display = $"{subjectCode} ({staff})";

                    if (!result.ContainsKey(day))
                        result[day] = new Dictionary<int, (string, string)>();

                    result[day][hour] = (display, subjectId);
                }

                // Fill all 5 weekdays and 7 hours
                var days = new[] { "Mon", "Tue", "Wed", "Thu", "Fri" };
                var periods = Enumerable.Range(1, 7).ToList();

                var timetable = days.Select(day => new
                {
                    Day = day,
                    HourlySlots = periods.ToDictionary(
                        p => p,
                        p => result.ContainsKey(day) && result[day].ContainsKey(p)
                            ? new { display = result[day][p].Display, subjectId = result[day][p].SubjectId }
                            : new { display = "---", subjectId = "0" }
                    )
                }).ToList();

                return Ok(new { timetable });
            }
            catch (Exception ex)
            {
                return StatusCode(500, new
                {
                    message = "❌ Internal Server Error while fetching timetable.",
                    error = ex.Message
                });
            }
        }



        [HttpGet("grouped1")]
        public async Task<IActionResult> GetAssignmentsForReceivingDepartment()
        {
            var connectionString = _configuration.GetConnectionString("DefaultConnection");

            try
            {
                var results = new List<dynamic>();
                using var conn = new NpgsqlConnection(connectionString);
                await conn.OpenAsync();

                var sql = @"
                SELECT 
    id,
    department,
    staff_department,
    subject_id,
    subject_shrt,
    year,
    sem,
    section,
    staffname
FROM pendingtimetabledata
WHERE staffname ='From Other Department'
ORDER BY 
    year,
    sem,
    section,
    department
";

                using var cmd = new NpgsqlCommand(sql, conn);
                using var reader = await cmd.ExecuteReaderAsync();

                while (await reader.ReadAsync())
                {
                    results.Add(new
                    {
                        id = Convert.ToInt32(reader["id"]),
                        fromDepartment = reader["department"].ToString(),
                        toDepartment = reader["staff_department"].ToString(),
                        subCode = reader["subject_id"].ToString(),
                        subjectName = reader["subject_shrt"].ToString(),
                        year = reader["year"].ToString(),
                        semester = reader["sem"].ToString(),
                        section = reader["section"].ToString(),
                        assignedStaff = reader["staffname"] == DBNull.Value ? null : reader["staffname"].ToString()
                    });
                }

                return Ok(results);
            }
            catch (Exception ex)
            {
                return StatusCode(500, new { message = "Failed to retrieve data", error = ex.Message });
            }
        }



        [HttpPost("save")]
        public async Task<IActionResult> SavePendingTimetableData([FromBody] TimetableSaveRequest request)
        {
            if (request?.Subjects == null || request.Subjects.Count == 0)
                return BadRequest(new { message = "No subjects provided" });

            var connectionString = _configuration.GetConnectionString("DefaultConnection");

            try
            {
                await using var conn = new NpgsqlConnection(connectionString);
                await conn.OpenAsync();

                await using var tran = await conn.BeginTransactionAsync();

                var sql = @"
            INSERT INTO pendingtimetabledata
            (staff_id, staffname, subject_id, lab_id, staff_department,
             subject_shrt, credit, subtype, department, year, sem, lab_department, section,status)
            VALUES
            (@staffId, @staffName, @subjectId, @labId, @staffDepartment,
             @subjectShortForm, @credit, @subType, @department, @year, @sem, @labDepartment, @section,@status);
        ";

                foreach (var subject in request.Subjects)
                {
                    bool isStaffOtherDept = !string.Equals(subject.StaffDepartment ?? "", request.Department ?? "", StringComparison.OrdinalIgnoreCase);
                    bool isLabOtherDept = !string.Equals(subject.LabDepartment ?? "", request.Department ?? "", StringComparison.OrdinalIgnoreCase);

                    await using var cmd = new NpgsqlCommand(sql, conn, tran);

                    // Fix for CS0019: Operator '??' cannot be applied to operands of type 'string' and 'DBNull'
                    // Updated the code to use a ternary operator to handle DBNull explicitly.

                    object staffIdValue = isStaffOtherDept ? (object)"From Other Department" : (subject.StaffId != null ? (object)subject.StaffId : DBNull.Value);
                    object staffNameValue = isStaffOtherDept ? (object)"From Other Department" : (subject.StaffName != null ? (object)subject.StaffName : DBNull.Value);
                    object labIdValue = isLabOtherDept ? (object)"From Other Department" : (subject.LabId != null ? (object)subject.LabId : DBNull.Value);

                    cmd.Parameters.AddWithValue("staffId", staffIdValue);
                    cmd.Parameters.AddWithValue("staffName", staffNameValue);
                    cmd.Parameters.AddWithValue("subjectId", subject.SubjectCode ?? "");
                    cmd.Parameters.AddWithValue("labId", labIdValue);
                    cmd.Parameters.AddWithValue("staffDepartment", subject.StaffDepartment ?? request.Department ?? "");
                    cmd.Parameters.AddWithValue("subjectShortForm", subject.SubjectShortForm ?? subject.SubjectCode ?? "");
                    cmd.Parameters.AddWithValue("credit", subject.Credit);
                    cmd.Parameters.AddWithValue("subType", subject.SubjectType ?? "");
                    cmd.Parameters.AddWithValue("department", request.Department ?? "");
                    cmd.Parameters.AddWithValue("year", request.Year ?? "");
                    cmd.Parameters.AddWithValue("sem", request.Semester ?? "");
                    cmd.Parameters.AddWithValue("labDepartment", subject.LabDepartment ?? ((subject.SubjectType == "Lab" || subject.SubjectType == "Embedded") ? request.Department : ""));
                    cmd.Parameters.AddWithValue("section", request.Section ?? "");
                    cmd.Parameters.AddWithValue("status", "pending");

                    await cmd.ExecuteNonQueryAsync();
                }

                await tran.CommitAsync();

                return Ok(new { message = "Pending timetable data saved successfully" });
            }
            catch (Exception ex)
            {
                return StatusCode(500, new { message = "Failed to save timetable data", error = ex.Message });
            }
        }

        public class TimetableSaveRequest
        {
            public string Department { get; set; } = string.Empty;
            public string Year { get; set; } = string.Empty;
            public string Semester { get; set; } = string.Empty;
            public string Section { get; set; } = string.Empty;
            public List<SubjectSaveData> Subjects { get; set; } = new List<SubjectSaveData>();
        }


        public class SubjectSaveData
        {
            public string? StaffId { get; set; }
            public string? StaffName { get; set; }
            public string? SubjectCode { get; set; }
            public string? LabId { get; set; }
            public string? StaffDepartment { get; set; }
            public string? SubjectShortForm { get; set; }
            public int Credit { get; set; }
            public string? SubjectType { get; set; }
            public string? LabDepartment { get; set; }
        }
    

        [HttpGet("store")]
        public IActionResult StoreAssignment(
     string subCode,
     string subjectName,
     string subjectType,
     int credit,
     string staffAssigned,
     string year,
     string semester,
     string section,
     string department,
     string? labId = null // ✅ Added labId
  )
        {
            try
            {
                var connectionString = _configuration.GetConnectionString("DefaultConnection");

                using var conn = new NpgsqlConnection(connectionString);
                conn.Open();

                var query = @"
            INSERT INTO subject_assignments
            (sub_code, subject_name, subject_type, credit, staff_assigned, year, semester, section, department, lab_id)
            VALUES (@sub_code, @subject_name, @subject_type, @credit, @staff_assigned, @year, @semester, @section, @department, @lab_id)";

                using var cmd = new NpgsqlCommand(query, conn);
                cmd.Parameters.AddWithValue("@sub_code", subCode);
                cmd.Parameters.AddWithValue("@subject_name", subjectName);
                cmd.Parameters.AddWithValue("@subject_type", subjectType);
                cmd.Parameters.AddWithValue("@credit", credit);
                cmd.Parameters.AddWithValue("@staff_assigned", staffAssigned ?? (object)DBNull.Value);
                cmd.Parameters.AddWithValue("@year", year);
                cmd.Parameters.AddWithValue("@semester", semester);
                cmd.Parameters.AddWithValue("@section", section);
                cmd.Parameters.AddWithValue("@department", department);
                cmd.Parameters.AddWithValue("@lab_id", (object?)labId ?? DBNull.Value); // ✅ Handle nulls

                cmd.ExecuteNonQuery();

                return Ok(new { message = "Assignment stored successfully." });
            }
            catch (Exception ex)
            {
                return StatusCode(500, new { message = "Error storing assignment", error = ex.Message });
            }
        }

        [HttpGet("getStaffTimetableById")]
        public async Task<IActionResult> GetStaffTimetableById([FromQuery] string staffId)
        {
            var connectionString = _configuration.GetConnectionString("DefaultConnection");

            try
            {
                using var conn = new NpgsqlConnection(connectionString);
                await conn.OpenAsync();

                string staffName = "---";
                var resultList = new List<object>();



                // join on sub_code (not subject_code) in subject_data2
                var query = @"
            SELECT
                st.staff_name,
                st.department_id,
                st.year,
                st.section,
                st.day,
                st.hour,
                st.subject_code,
                sd.subject_shortform
            FROM classtimetable AS st
            LEFT JOIN subject_data AS sd
              ON LOWER(st.subject_code) = LOWER(sd.subject_code)
            WHERE LOWER(st.staff_code) = LOWER(@staffId)
            ORDER BY st.day, st.hour;
        ";

                using var cmd = new NpgsqlCommand(query, conn);
                cmd.Parameters.AddWithValue("@staffId", staffId.Trim());

                using var reader = await cmd.ExecuteReaderAsync();
                while (await reader.ReadAsync())
                {
                    // capture the first occurrence of staff_name
                    if (staffName == "---")
                        staffName = reader["staff_name"]?.ToString() ?? "---";

                    resultList.Add(new
                    {
                        Department = reader["department_id"].ToString(),
                        Year = reader["year"].ToString(),
                        Section = reader["section"].ToString(),
                        Day = reader["day"].ToString(),
                        Hour = Convert.ToInt32(reader["hour"]),
                        SubjectCode = reader["subject_code"].ToString(),
                        SubjectId = reader["subject_code"] == DBNull.Value
                                          ? ""
                                          : reader["subject_code"].ToString()
                    });
                }

                if (resultList.Count == 0)
                {
                    return NotFound(new
                    {
                        message = "❌ No timetable found for given staff ID.",
                        staffId
                    });
                }

                return Ok(new
                {
                    staffId,
                    staffName,
                    records = resultList
                });
            }
            catch (Exception ex)
            {
                return StatusCode(500, new
                {
                    message = "❌ Error occurred while fetching staff timetable.",
                    error = ex.Message
                });
            }
        }

        [HttpGet("periods")]
        public async Task<IActionResult> GetStaffSubjectPeriodCounts()
        {

            var connectionString = _configuration.GetConnectionString("DefaultConnection");
            var result = new List<object>();
            var query = @"
    SELECT 
        staff_code, 
        staff_name, 
        department_id,
        subject_code, 
        subject_name, 
        COUNT(*) AS period_count
    FROM classtimetable
    GROUP BY staff_code, staff_name, department_id, subject_code, subject_name
    ORDER BY staff_code, subject_name;
";

            try
            {
                using var conn = new NpgsqlConnection(connectionString);
                await conn.OpenAsync();

                using var cmd = new NpgsqlCommand(query, conn);
                using var reader = await cmd.ExecuteReaderAsync();

                while (await reader.ReadAsync())
                {
                    result.Add(new
                    {
                        staffId = reader["staff_code"].ToString(),
                        staffName = reader["staff_name"].ToString(),
                        department = reader["department_id"].ToString(),
                        subjectCode = reader["subject_code"].ToString(),
                        subjectName = reader["subject_name"].ToString(),
                        count = Convert.ToInt32(reader["period_count"])  // Safe conversion from Int64 to Int32
                    });
                }

                return Ok(result);
            }
            catch (System.Exception ex)
            {
                return StatusCode(500, new
                {
                    message = "❌ Failed to retrieve staff period counts",
                    error = ex.Message
                });
            }




        }



        [HttpPut("update")]
        public async Task<IActionResult> UpdateStaff([FromBody] StaffDto staff)
        {
            var connString = _configuration.GetConnectionString("DefaultConnection");
            try
            {
                using var conn = new NpgsqlConnection(connString);
                await conn.OpenAsync();


                var query = @"
            UPDATE staff_data SET
              staffname = @name,
              prefsub1 = @subject1,
              prefsub2 = @subject2,
              prefsub3 = @subject3
            WHERE staffid = @staffId;
        ";

                using var cmd = new NpgsqlCommand(query, conn);
                cmd.Parameters.AddWithValue("@name", staff.name);
                cmd.Parameters.AddWithValue("@subject1", staff.subject1 ?? "");
                cmd.Parameters.AddWithValue("@subject2", staff.subject2 ?? "");
                cmd.Parameters.AddWithValue("@subject3", staff.subject3 ?? "");
                cmd.Parameters.AddWithValue("@staffId", staff.staffId);

                int affected = await cmd.ExecuteNonQueryAsync();
                return Ok(new { message = affected > 0 ? "✅ Updated" : "❌ Not found" });
            }
            catch (Exception ex)
            {
                return StatusCode(500, new { message = "❌ Error updating staff", error = ex.Message });
            }


        }

        //backtrack waste code
        //
        //[HttpPost("generateCrossDepartmentTimetable")]
        //public async Task<IActionResult> GenerateCrossDepartmentTimetableHybrid([FromBody] TimetableRequest request)
        //{
        //    var connectionString = _configuration.GetConnectionString("DefaultConnection");

        //    try
        //    {
        //        var subjects = new List<TimetableGA.TimetableEngine.Subject>();
        //        var globalStaffAvailability = new Dictionary<string, Dictionary<string, HashSet<int>>>();
        //        var labAvailability = new Dictionary<string, Dictionary<string, HashSet<int>>>();
        //        var labTimetable = new List<object>();

        //        using var conn = new NpgsqlConnection(connectionString);
        //        await conn.OpenAsync();

        //        // Build subjects
        //        foreach (var sub in request.Subjects)
        //        {
        //            if (string.IsNullOrWhiteSpace(sub.StaffAssigned)) continue;

        //            subjects.Add(new TimetableGA.TimetableEngine.Subject
        //            {
        //                SubjectCode = sub.SubjectCode ?? "---",
        //                SubjectName = sub.SubjectName ?? "---",
        //                SubjectType = sub.SubjectType ?? "Theory",
        //                Credit = sub.Credit,
        //                StaffAssigned = sub.StaffAssigned,
        //                LabId = (sub.SubjectType?.ToLower() == "lab" || sub.SubjectType?.ToLower() == "embedded")
        //                    ? (sub.LabId != null ? sub.LabId.Trim() : null)
        //                    : null
        //            });
        //        }

        //        if (subjects.Count == 0)
        //        {
        //            return BadRequest(new
        //            {
        //                message = "❌ No valid subjects found. Ensure all have assigned staff.",
        //                receivedPayload = request
        //            });
        //        }

        //        // Load current lab availability
        //        var labAvailabilityQuery = "SELECT lab_id, day, hour FROM labtimetable";
        //        using (var labCmd = new NpgsqlCommand(labAvailabilityQuery, conn))
        //        using (var labReader = await labCmd.ExecuteReaderAsync())
        //        {
        //            while (await labReader.ReadAsync())
        //            {
        //                var labId = labReader["lab_id"].ToString();
        //                var day = labReader["day"].ToString();
        //                var hour = Convert.ToInt32(labReader["hour"]);

        //                if (!labAvailability.ContainsKey(labId))
        //                {
        //                    labAvailability[labId] = new Dictionary<string, HashSet<int>>();
        //                    foreach (var d in new[] { "Mon", "Tue", "Wed", "Thu", "Fri" })
        //                        labAvailability[labId][d] = new HashSet<int>();
        //                }
        //                labAvailability[labId][day].Add(hour);
        //            }
        //        }

        //        // Load current staff availability
        //        var availabilityQuery = "SELECT staff_code, day, hour FROM classtimetable";
        //        using (var staffCmd = new NpgsqlCommand(availabilityQuery, conn))
        //        using (var reader = await staffCmd.ExecuteReaderAsync())
        //        {
        //            while (await reader.ReadAsync())
        //            {
        //                var staff = reader["staff_code"].ToString();
        //                var day = reader["day"].ToString();
        //                var hour = Convert.ToInt32(reader["hour"]);

        //                if (!globalStaffAvailability.ContainsKey(staff))
        //                {
        //                    globalStaffAvailability[staff] = new();
        //                    foreach (var d in new[] { "Mon", "Tue", "Wed", "Thu", "Fri" })
        //                        globalStaffAvailability[staff][d] = new();
        //                }
        //                globalStaffAvailability[staff][day].Add(hour);
        //            }
        //        }

        //        // 1. Run GA to generate new timetable
        //        var engine = new TimetableGA.TimetableEngine();
        //        engine.Initialize(subjects, globalStaffAvailability, labAvailability);
        //        var (timetable, conflicts, bestChromosome) = engine.GenerateGA();

        //        // 2. COLLECT new INSERTS as rows to be inserted
        //        var newClassRows = new List<(string staffName, string staffId, TimetableGA.TimetableEngine.Subject subj, string day, int hour)>();

        //        foreach (var daySlot in timetable)
        //        {
        //            foreach (var kv in daySlot.HourlySlots)
        //            {
        //                string value = kv.Value;
        //                if (value == "---") continue;

        //                var hour = kv.Key;
        //                var parts = value.Split('(', StringSplitOptions.TrimEntries);
        //                string subjectCode = parts[0].Trim();

        //                var matchedSubject = subjects.FirstOrDefault(s => s.SubjectCode.Trim() == subjectCode);
        //                if (matchedSubject == null) continue;

        //                string fullStaff = matchedSubject.StaffAssigned ?? "---";
        //                string staffName = "---", staffId = "---";
        //                if (fullStaff.Contains("("))
        //                {
        //                    var nameId = fullStaff.Split('(', StringSplitOptions.TrimEntries);
        //                    staffName = nameId[0].Trim();
        //                    staffId = nameId[1].Replace(")", "").Trim();
        //                }

        //                newClassRows.Add((staffName, staffId, matchedSubject, daySlot.Day, hour));
        //            }
        //        }

        //        // 3. COLLECT new LAB INSERTS as rows to be inserted
        //        var newLabRows = new List<(string labId, TimetableGA.TimetableEngine.Subject subj, string staffAssigned, string day, int hour)>();
        //        foreach (var gene in bestChromosome.Genes)
        //        {
        //            if (gene.IsLabBlock && !string.IsNullOrEmpty(gene.LabId))
        //            {
        //                var subject = subjects.FirstOrDefault(s => s.SubjectCode == gene.SubjectCode);
        //                if (subject == null) continue;

        //                for (int h = gene.StartHour; h < gene.StartHour + gene.Duration; h++)
        //                {
        //                    newLabRows.Add((gene.LabId, subject, gene.StaffAssigned, gene.Day, h));
        //                }
        //            }
        //        }

        //        // 4. CHECK FOR CONFLICTS WITH EXISTING DB

        //        // -- Find all classtimetable and labtimetable rows that would conflict
        //        var conflictingClassRows = new List<int>(); // Store IDs of conflicting class timetable rows
        //        var conflictingLabRows = new List<int>();   // Store IDs of conflicting lab timetable rows

        //        // Conflict check for class timetable
        //        foreach (var row in newClassRows)
        //        {
        //            var cmdTxt = @"
        //                    SELECT id FROM classtimetable
        //                    WHERE staff_code = @staff_code AND day = @day AND hour = @hour";
        //            using var cmd = new NpgsqlCommand(cmdTxt, conn);
        //            cmd.Parameters.AddWithValue("@staff_code", row.staffId);
        //            cmd.Parameters.AddWithValue("@day", row.day);
        //            cmd.Parameters.AddWithValue("@hour", row.hour);

        //            using var rdr = await cmd.ExecuteReaderAsync();
        //            while (await rdr.ReadAsync())
        //            {
        //                conflictingClassRows.Add(rdr.GetInt32(0));
        //            }
        //            rdr.Close();
        //        }

        //        // Conflict check for lab timetable
        //        foreach (var row in newLabRows)
        //        {
        //            var cmdTxt = @"
        //                    SELECT id FROM labtimetable
        //                    WHERE lab_id = @lab_id AND day = @day AND hour = @hour";
        //            using var cmd = new NpgsqlCommand(cmdTxt, conn);
        //            cmd.Parameters.AddWithValue("@lab_id", row.labId);
        //            cmd.Parameters.AddWithValue("@day", row.day);
        //            cmd.Parameters.AddWithValue("@hour", row.hour);

        //            using var rdr = await cmd.ExecuteReaderAsync();
        //            while (await rdr.ReadAsync())
        //            {
        //                conflictingLabRows.Add(rdr.GetInt32(0));
        //            }
        //            rdr.Close();
        //        }

        //        // Backup and delete conflicting class timetable rows
        //        List<object> backupClassRows = new();
        //        foreach (var rowId in conflictingClassRows.Distinct())
        //        {
        //            // Backup to _backup table
        //            var backupCmd = new NpgsqlCommand(
        //                @"INSERT INTO classtimetable_backup SELECT * FROM classtimetable WHERE id=@id", conn);
        //            backupCmd.Parameters.AddWithValue("@id", rowId);
        //            await backupCmd.ExecuteNonQueryAsync();

        //            // Read for rescheduling
        //            var selCmd = new NpgsqlCommand(
        //                @"SELECT * FROM classtimetable WHERE id=@id", conn);
        //            selCmd.Parameters.AddWithValue("@id", rowId);

        //            using var rdr = await selCmd.ExecuteReaderAsync();
        //            if (await rdr.ReadAsync())
        //            {
        //                // Keep minimal info necessary for rescheduling
        //                backupClassRows.Add(new
        //                {
        //                    staff_name = rdr["staff_name"].ToString(),
        //                    staff_code = rdr["staff_code"].ToString(),
        //                    department_id = rdr["department_id"].ToString(),
        //                    year = rdr["year"].ToString(),
        //                    semester = rdr["semester"].ToString(),
        //                    section = rdr["section"].ToString(),
        //                    day = rdr["day"].ToString(),
        //                    hour = Convert.ToInt32(rdr["hour"]),
        //                    subject_code = rdr["subject_code"].ToString(),
        //                    subject_name = rdr["subject_name"].ToString()
        //                });
        //            }
        //            rdr.Close();

        //            // Delete
        //            var delCmd = new NpgsqlCommand(
        //                @"DELETE FROM classtimetable WHERE id=@id", conn);
        //            delCmd.Parameters.AddWithValue("@id", rowId);
        //            await delCmd.ExecuteNonQueryAsync();
        //        }

        //        // Backup and delete conflicting lab timetable rows
        //        List<object> backupLabRows = new();
        //        foreach (var rowId in conflictingLabRows.Distinct())
        //        {
        //            // Backup to _backup table
        //            var backupCmd = new NpgsqlCommand(
        //                @"INSERT INTO labtimetable_backup SELECT * FROM labtimetable WHERE id=@id", conn);
        //            backupCmd.Parameters.AddWithValue("@id", rowId);
        //            await backupCmd.ExecuteNonQueryAsync();

        //            // Read for rescheduling
        //            var selCmd = new NpgsqlCommand(
        //                @"SELECT * FROM labtimetable WHERE id=@id", conn);
        //            selCmd.Parameters.AddWithValue("@id", rowId);
        //            using var rdr = await selCmd.ExecuteReaderAsync();
        //            if (await rdr.ReadAsync())
        //            {
        //                backupLabRows.Add(new
        //                {
        //                    lab_id = rdr["lab_id"].ToString(),
        //                    subject_code = rdr["subject_code"].ToString(),
        //                    subject_name = rdr["subject_name"].ToString(),
        //                    staff_name = rdr["staff_name"].ToString(),
        //                    department = rdr["department"].ToString(),
        //                    year = rdr["year"].ToString(),
        //                    semester = rdr["semester"].ToString(),
        //                    section = rdr["section"].ToString(),
        //                    day = rdr["day"].ToString(),
        //                    hour = Convert.ToInt32(rdr["hour"])
        //                });
        //            }
        //            rdr.Close();

        //            // Delete
        //            var delCmd = new NpgsqlCommand(
        //                @"DELETE FROM labtimetable WHERE id=@id", conn);
        //            delCmd.Parameters.AddWithValue("@id", rowId);
        //            await delCmd.ExecuteNonQueryAsync();
        //        }

        //        // Now insert new rows
        //        foreach (var (staffName, staffId, matchedSubject, day, hour) in newClassRows)
        //        {
        //            var staffInsertCmd = new NpgsqlCommand(@"
        //                    INSERT INTO classtimetable
        //                    (staff_name, staff_code, department_id, year, semester, section, day, hour, subject_code, subject_name)
        //                    VALUES
        //                    (@staff_name, @staff_id, @department, @year, @semester, @section, @day, @hour, @subject_code, @subject_name);", conn);
        //            staffInsertCmd.Parameters.AddWithValue("@staff_name", staffName);
        //            staffInsertCmd.Parameters.AddWithValue("@staff_id", staffId);
        //            staffInsertCmd.Parameters.AddWithValue("@department", request.Department);
        //            staffInsertCmd.Parameters.AddWithValue("@year", request.Year);
        //            staffInsertCmd.Parameters.AddWithValue("@semester", request.Semester);
        //            staffInsertCmd.Parameters.AddWithValue("@section", request.Section);
        //            staffInsertCmd.Parameters.AddWithValue("@day", day);
        //            staffInsertCmd.Parameters.AddWithValue("@hour", hour);
        //            staffInsertCmd.Parameters.AddWithValue("@subject_code", matchedSubject.SubjectCode);
        //            staffInsertCmd.Parameters.AddWithValue("@subject_name", matchedSubject.SubjectName);
        //            await staffInsertCmd.ExecuteNonQueryAsync();
        //        }

        //        foreach (var (labId, subject, staffAssigned, day, hour) in newLabRows)
        //        {
        //            var labInsertCmd = new NpgsqlCommand(@"
        //                    INSERT INTO labtimetable
        //                    (lab_id, subject_code, subject_name, staff_name, department, year, semester, section, day, hour)
        //                    VALUES
        //                    (@lab_id, @subject_code, @subject_name, @staff_name, @department, @year, @semester, @section, @day, @hour);", conn);

        //            labInsertCmd.Parameters.AddWithValue("@lab_id", labId);
        //            labInsertCmd.Parameters.AddWithValue("@subject_code", subject.SubjectCode);
        //            labInsertCmd.Parameters.AddWithValue("@subject_name", subject.SubjectName);
        //            labInsertCmd.Parameters.AddWithValue("@staff_name", staffAssigned ?? "---");
        //            labInsertCmd.Parameters.AddWithValue("@department", request.Department);
        //            labInsertCmd.Parameters.AddWithValue("@year", request.Year);
        //            labInsertCmd.Parameters.AddWithValue("@semester", request.Semester);
        //            labInsertCmd.Parameters.AddWithValue("@section", request.Section);
        //            labInsertCmd.Parameters.AddWithValue("@day", day);
        //            labInsertCmd.Parameters.AddWithValue("@hour", hour);
        //            await labInsertCmd.ExecuteNonQueryAsync();

        //            labTimetable.Add(new
        //            {
        //                lab_id = labId,
        //                subject_code = subject.SubjectCode,
        //                subject_name = subject.SubjectName,
        //                staff_assigned = staffAssigned,
        //                department = request.Department,
        //                year = request.Year,
        //                semester = request.Semester,
        //                section = request.Section,
        //                day = day,
        //                hour = hour
        //            });
        //        }

        //        // Try to reschedule backup class rows (if any) by running GA, now including the current timetable constraints!
        //        var unscheduledClassRows = new List<object>();
        //        if (backupClassRows.Count > 0)
        //        {
        //            // For each class backup, try to find a new slot using the current GA availability
        //            foreach (var entry in backupClassRows)
        //            {
        //                // Build a 'subject' for GA
        //                var backupSubject = new TimetableGA.TimetableEngine.Subject
        //                {
        //                    SubjectCode = (string)entry.GetType().GetProperty("subject_code").GetValue(entry),
        //                    SubjectName = (string)entry.GetType().GetProperty("subject_name").GetValue(entry),
        //                    Credit = 1,
        //                    StaffAssigned = (string)entry.GetType().GetProperty("staff_code").GetValue(entry),
        //                    SubjectType = "Theory" // for backup class timetable, you may want to customize if needed
        //                };

        //                // Prepare limited subject and availability for this entry
        //                var singleSubList = new List<TimetableGA.TimetableEngine.Subject> { backupSubject };
        //                var singleStaffAvail = await LoadAvailabilityForStaff(backupSubject.StaffAssigned, conn);

        //                var ga = new TimetableGA.TimetableEngine();
        //                ga.Initialize(singleSubList, singleStaffAvail, new Dictionary<string, Dictionary<string, HashSet<int>>>());
        //                var (tt, conflicts2, chrom) = ga.GenerateGA();

        //                if (conflicts2.Count == 0)
        //                {
        //                    // insert
        //                    foreach (var slot in tt)
        //                    {
        //                        foreach (var kv in slot.HourlySlots)
        //                        {
        //                            if (kv.Value == "---") continue;
        //                            var cmd2 = new NpgsqlCommand(@"
        //                                    INSERT INTO classtimetable
        //                                    (staff_name, staff_code, department_id, year, semester, section, day, hour, subject_code, subject_name)
        //                                    VALUES
        //                                    (@staff_name, @staff_code, @department_id, @year, @semester, @section, @day, @hour, @subject_code, @subject_name);", conn);
        //                            cmd2.Parameters.AddWithValue("@staff_name", entry.GetType().GetProperty("staff_name").GetValue(entry));
        //                            cmd2.Parameters.AddWithValue("@staff_code", entry.GetType().GetProperty("staff_code").GetValue(entry));
        //                            cmd2.Parameters.AddWithValue("@department_id", entry.GetType().GetProperty("department_id").GetValue(entry));
        //                            cmd2.Parameters.AddWithValue("@year", entry.GetType().GetProperty("year").GetValue(entry));
        //                            cmd2.Parameters.AddWithValue("@semester", entry.GetType().GetProperty("semester").GetValue(entry));
        //                            cmd2.Parameters.AddWithValue("@section", entry.GetType().GetProperty("section").GetValue(entry));
        //                            cmd2.Parameters.AddWithValue("@day", slot.Day);
        //                            cmd2.Parameters.AddWithValue("@hour", kv.Key);
        //                            cmd2.Parameters.AddWithValue("@subject_code", backupSubject.SubjectCode);
        //                            cmd2.Parameters.AddWithValue("@subject_name", backupSubject.SubjectName);
        //                            await cmd2.ExecuteNonQueryAsync();
        //                        }
        //                    }
        //                }
        //                else
        //                {
        //                    unscheduledClassRows.Add(entry);
        //                }
        //            }
        //        }

        //        // The same pattern can be used for backupLabRows if needed

        //        return Ok(new
        //        {
        //            message = conflicts.Count == 0 && unscheduledClassRows.Count == 0
        //                ? "✅ Hybrid Timetable generated and stored successfully; all conflicts resolved."
        //                : "⚠ Hybrid Timetable generated with some unsolved/conflicting cases. Some original class/lab slots could not be rescheduled.",
        //            timetable,
        //            labTimetable,
        //            unresolvedClassConflicts = unscheduledClassRows,
        //            // unresolvedLabConflicts = unscheduledLabRows, // to implement as per above
        //            usedLabIds = subjects.Where(s => !string.IsNullOrEmpty(s.LabId)).Select(s => s.LabId).Distinct().ToList(),
        //            receivedPayload = request,
        //            conflicts = conflicts.Select(c => new
        //            {
        //                subject = c.Subject?.SubjectCode,
        //                staff = c.Subject?.StaffAssigned,
        //                reason = c.Reason
        //            })
        //        });
        //    }
        //    catch (Exception ex)
        //    {
        //        return StatusCode(500, new
        //        {
        //            message = "❌ Internal Server Error while generating timetable.",
        //            error = ex.Message
        //        });
        //    }
        //}
       
        //backtrack waste code
        [HttpGet("generateCrossDepartmentTimetable")]
        public async Task<IActionResult> GenerateCrossDepartmentTimetable(
                            [FromQuery] string toDepartment,
                            [FromQuery] string year,
                            [FromQuery] string semester,
                            [FromQuery] string section)
        {
            var connectionString = _configuration.GetConnectionString("DefaultConnection");

            try
            {
                var subjects = new List<TimetableGA.TimetableEngine.Subject>();
                var globalStaffAvailability = new Dictionary<string, Dictionary<string, HashSet<int>>>();
                var labAvailability = new Dictionary<string, Dictionary<string, HashSet<int>>>();
                var labTimetable = new List<object>();

                using var conn = new NpgsqlConnection(connectionString);
                await conn.OpenAsync();

                // -- SUBJECT LOADING: this is the ONLY part that differs from POST! --
                // GET: load from pendingtimetabledata using query params
                var selectQuery = @"
                         SELECT staffname, subject_id, lab_id, staff_department, subject_shrt,
                                credit, subtype, department, year, sem, lab_department, section
                         FROM pendingtimetabledata
                         WHERE department = @dept AND year = @year AND sem = @sem AND section = @section";

                using (var cmd = new NpgsqlCommand(selectQuery, conn))
                {
                    cmd.Parameters.AddWithValue("@dept", toDepartment);
                    cmd.Parameters.AddWithValue("@year", year);
                    cmd.Parameters.AddWithValue("@sem", semester);
                    cmd.Parameters.AddWithValue("@section", section);

                    using var reader = await cmd.ExecuteReaderAsync();
                    while (await reader.ReadAsync())
                    {
                        var staffFull = reader["staffname"].ToString();
                        if (string.IsNullOrWhiteSpace(staffFull)) continue;

                        subjects.Add(new TimetableGA.TimetableEngine.Subject
                        {
                            SubjectCode = reader["subject_id"].ToString() ?? "---",
                            SubjectName = reader["subject_shrt"].ToString() ?? "---",
                            SubjectType = reader["subtype"].ToString() ?? "Theory",
                            Credit = Convert.ToInt32(reader["credit"]),
                            StaffAssigned = staffFull,
                            LabId = (reader["subtype"].ToString()?.ToLower() == "lab" ||
                                     reader["subtype"].ToString()?.ToLower() == "embedded")
                                    ? reader["lab_id"]?.ToString()?.Trim()
                                    : null
                        });
                    }
                    reader.Close();
                }
                // -- END OF SUBJECT LOADING --

                if (subjects.Count == 0)
                {
                    return BadRequest(new
                    {
                        message = "❌ No valid subjects found in pendingtimetabledata.",
                        receivedPayload = new { toDepartment, year, semester, section }
                    });
                }

                // Load current lab availability
                var labAvailabilityQuery = "SELECT lab_id, day, hour FROM labtimetable";
                using (var labCmd = new NpgsqlCommand(labAvailabilityQuery, conn))
                using (var labReader = await labCmd.ExecuteReaderAsync())
                {
                    while (await labReader.ReadAsync())
                    {
                        var labId = labReader["lab_id"].ToString();
                        var day = labReader["day"].ToString();
                        var hour = Convert.ToInt32(labReader["hour"]);

                        if (!labAvailability.ContainsKey(labId))
                        {
                            labAvailability[labId] = new Dictionary<string, HashSet<int>>();
                            foreach (var d in new[] { "Mon", "Tue", "Wed", "Thu", "Fri" })
                                labAvailability[labId][d] = new HashSet<int>();
                        }
                        labAvailability[labId][day].Add(hour);
                    }
                }

                // Load current staff availability
                var availabilityQuery = "SELECT staff_code, day, hour FROM classtimetable";
                using (var staffCmd = new NpgsqlCommand(availabilityQuery, conn))
                using (var staffReader = await staffCmd.ExecuteReaderAsync())
                {
                    while (await staffReader.ReadAsync())
                    {
                        var staff = staffReader["staff_code"].ToString();
                        var day = staffReader["day"].ToString();
                        var hour = Convert.ToInt32(staffReader["hour"]);

                        if (!globalStaffAvailability.ContainsKey(staff))
                        {
                            globalStaffAvailability[staff] = new();
                            foreach (var d in new[] { "Mon", "Tue", "Wed", "Thu", "Fri" })
                                globalStaffAvailability[staff][d] = new();
                        }
                        globalStaffAvailability[staff][day].Add(hour);
                    }
                }

                // 1. Run GA to generate new timetable
                var engine = new TimetableGA.TimetableEngine();
                engine.Initialize(subjects, globalStaffAvailability, labAvailability);
                var (timetable, conflicts, bestChromosome) = engine.GenerateGA();

                // 2. COLLECT new INSERTS as rows to be inserted
                var newClassRows = new List<(string staffName, string staffId, TimetableGA.TimetableEngine.Subject subj, string day, int hour)>();

                foreach (var daySlot in timetable)
                {
                    foreach (var kv in daySlot.HourlySlots)
                    {
                        string value = kv.Value;
                        if (value == "---") continue;

                        var hour = kv.Key;
                        var parts = value.Split('(', StringSplitOptions.TrimEntries);
                        string subjectCode = parts[0].Trim();

                        var matchedSubject = subjects.FirstOrDefault(s => s.SubjectCode.Trim() == subjectCode);
                        if (matchedSubject == null) continue;

                        string fullStaff = matchedSubject.StaffAssigned ?? "---";
                        string staffName = "---", staffId = "---";
                        if (fullStaff.Contains("("))
                        {
                            var nameId = fullStaff.Split('(', StringSplitOptions.TrimEntries);
                            staffName = nameId[0].Trim();
                            staffId = nameId[1].Replace(")", "").Trim();
                        }
                        else
                        {
                            staffName = fullStaff.Trim();
                            staffId = "---";
                        }

                        newClassRows.Add((staffName, staffId, matchedSubject, daySlot.Day, hour));
                    }
                }

                // 3. COLLECT new LAB INSERTS as rows to be inserted
                var newLabRows = new List<(string labId, TimetableGA.TimetableEngine.Subject subj, string staffAssigned, string day, int hour)>();
                foreach (var gene in bestChromosome.Genes)
                {
                    if (gene.IsLabBlock && !string.IsNullOrEmpty(gene.LabId))
                    {
                        var subject = subjects.FirstOrDefault(s => s.SubjectCode == gene.SubjectCode);
                        if (subject == null) continue;

                        for (int h = gene.StartHour; h < gene.StartHour + gene.Duration; h++)
                        {
                            newLabRows.Add((gene.LabId, subject, gene.StaffAssigned, gene.Day, h));
                        }
                    }
                }

                // 4. CHECK FOR CONFLICTS WITH EXISTING DB

                // -- Find all classtimetable and labtimetable rows that would conflict
                var conflictingClassRows = new List<int>(); // Store IDs of conflicting class timetable rows
                var conflictingLabRows = new List<int>();   // Store IDs of conflicting lab timetable rows

                // Conflict check for class timetable
                foreach (var row in newClassRows)
                {
                    var cmdTxt = @"
                             SELECT id FROM classtimetable
                             WHERE staff_code = @staff_code AND day = @day AND hour = @hour";
                    using var conflictCmd = new NpgsqlCommand(cmdTxt, conn);
                    conflictCmd.Parameters.AddWithValue("@staff_code", row.staffId);
                    conflictCmd.Parameters.AddWithValue("@day", row.day);
                    conflictCmd.Parameters.AddWithValue("@hour", row.hour);

                    using var rdr = await conflictCmd.ExecuteReaderAsync();
                    while (await rdr.ReadAsync())
                    {
                        conflictingClassRows.Add(rdr.GetInt32(0));
                    }
                    rdr.Close();
                }

                // Conflict check for lab timetable
                foreach (var row in newLabRows)
                {
                    var cmdTxt = @"
                             SELECT id FROM labtimetable
                             WHERE lab_id = @lab_id AND day = @day AND hour = @hour";
                    using var conflictCmd = new NpgsqlCommand(cmdTxt, conn);
                    conflictCmd.Parameters.AddWithValue("@lab_id", row.labId);
                    conflictCmd.Parameters.AddWithValue("@day", row.day);
                    conflictCmd.Parameters.AddWithValue("@hour", row.hour);

                    using var rdr = await conflictCmd.ExecuteReaderAsync();
                    while (await rdr.ReadAsync())
                    {
                        conflictingLabRows.Add(rdr.GetInt32(0));
                    }
                    rdr.Close();
                }

                // Backup and delete conflicting class timetable rows
                List<object> backupClassRows = new();
                foreach (var rowId in conflictingClassRows.Distinct())
                {
                    var backupCmd = new NpgsqlCommand(
                        @"INSERT INTO classtimetable_backup SELECT * FROM classtimetable WHERE id=@id", conn);
                    backupCmd.Parameters.AddWithValue("@id", rowId);
                    await backupCmd.ExecuteNonQueryAsync();

                    var selCmd = new NpgsqlCommand(
                        @"SELECT * FROM classtimetable WHERE id=@id", conn);
                    selCmd.Parameters.AddWithValue("@id", rowId);

                    using var rdr = await selCmd.ExecuteReaderAsync();
                    if (await rdr.ReadAsync())
                    {
                        backupClassRows.Add(new
                        {
                            staff_name = rdr["staff_name"].ToString(),
                            staff_code = rdr["staff_code"].ToString(),
                            department_id = rdr["department_id"].ToString(),
                            year = rdr["year"].ToString(),
                            semester = rdr["semester"].ToString(),
                            section = rdr["section"].ToString(),
                            day = rdr["day"].ToString(),
                            hour = Convert.ToInt32(rdr["hour"]),
                            subject_code = rdr["subject_code"].ToString(),
                            subject_name = rdr["subject_name"].ToString()
                        });
                    }
                    rdr.Close();

                    var delCmd = new NpgsqlCommand(
                        @"DELETE FROM classtimetable WHERE id=@id", conn);
                    delCmd.Parameters.AddWithValue("@id", rowId);
                    await delCmd.ExecuteNonQueryAsync();
                }

                // Backup and delete conflicting lab timetable rows
                List<object> backupLabRows = new();
                foreach (var rowId in conflictingLabRows.Distinct())
                {
                    var backupCmd = new NpgsqlCommand(
                        @"INSERT INTO labtimetable_backup SELECT * FROM labtimetable WHERE id=@id", conn);
                    backupCmd.Parameters.AddWithValue("@id", rowId);
                    await backupCmd.ExecuteNonQueryAsync();

                    var selCmd = new NpgsqlCommand(@"SELECT * FROM labtimetable WHERE id=@id", conn);
                    selCmd.Parameters.AddWithValue("@id", rowId);
                    using var rdr = await selCmd.ExecuteReaderAsync();
                    if (await rdr.ReadAsync())
                    {
                        backupLabRows.Add(new
                        {
                            lab_id = rdr["lab_id"].ToString(),
                            subject_code = rdr["subject_code"].ToString(),
                            subject_name = rdr["subject_name"].ToString(),
                            staff_name = rdr["staff_name"].ToString(),
                            department = rdr["department"].ToString(),
                            year = rdr["year"].ToString(),
                            semester = rdr["semester"].ToString(),
                            section = rdr["section"].ToString(),
                            day = rdr["day"].ToString(),
                            hour = Convert.ToInt32(rdr["hour"])
                        });
                    }
                    rdr.Close();

                    var delCmd = new NpgsqlCommand(@"DELETE FROM labtimetable WHERE id=@id", conn);
                    delCmd.Parameters.AddWithValue("@id", rowId);
                    await delCmd.ExecuteNonQueryAsync();
                }

                // Now insert new rows
                foreach (var (staffName, staffId, matchedSubject, day, hour) in newClassRows)
                {
                    var staffInsertCmd = new NpgsqlCommand(@"
                             INSERT INTO classtimetable
                             (staff_name, staff_code, department_id, year, semester, section, day, hour, subject_code, subject_name)
                             VALUES
                             (@staff_name, @staff_id, @department, @year, @semester, @section, @day, @hour, @subject_code, @subject_name);", conn);
                    staffInsertCmd.Parameters.AddWithValue("@staff_name", staffName);
                    staffInsertCmd.Parameters.AddWithValue("@staff_id", staffId);
                    staffInsertCmd.Parameters.AddWithValue("@department", toDepartment);
                    staffInsertCmd.Parameters.AddWithValue("@year", year);
                    staffInsertCmd.Parameters.AddWithValue("@semester", semester);
                    staffInsertCmd.Parameters.AddWithValue("@section", section);
                    staffInsertCmd.Parameters.AddWithValue("@day", day);
                    staffInsertCmd.Parameters.AddWithValue("@hour", hour);
                    staffInsertCmd.Parameters.AddWithValue("@subject_code", matchedSubject.SubjectCode);
                    staffInsertCmd.Parameters.AddWithValue("@subject_name", matchedSubject.SubjectName);
                    await staffInsertCmd.ExecuteNonQueryAsync();
                }

                foreach (var (labId, subject, staffAssigned, day, hour) in newLabRows)
                {
                    var labInsertCmd = new NpgsqlCommand(@"
                             INSERT INTO labtimetable
                             (lab_id, subject_code, subject_name, staff_name, department, year, semester, section, day, hour)
                             VALUES
                             (@lab_id, @subject_code, @subject_name, @staff_name, @department, @year, @semester, @section, @day, @hour);", conn);

                    labInsertCmd.Parameters.AddWithValue("@lab_id", labId);
                    labInsertCmd.Parameters.AddWithValue("@subject_code", subject.SubjectCode);
                    labInsertCmd.Parameters.AddWithValue("@subject_name", subject.SubjectName);
                    labInsertCmd.Parameters.AddWithValue("@staff_name", staffAssigned ?? "---");
                    labInsertCmd.Parameters.AddWithValue("@department", toDepartment);
                    labInsertCmd.Parameters.AddWithValue("@year", year);
                    labInsertCmd.Parameters.AddWithValue("@semester", semester);
                    labInsertCmd.Parameters.AddWithValue("@section", section);
                    labInsertCmd.Parameters.AddWithValue("@day", day);
                    labInsertCmd.Parameters.AddWithValue("@hour", hour);
                    await labInsertCmd.ExecuteNonQueryAsync();

                    labTimetable.Add(new
                    {
                        lab_id = labId,
                        subject_code = subject.SubjectCode,
                        subject_name = subject.SubjectName,
                        staff_assigned = staffAssigned,
                        department = toDepartment,
                        year = year,
                        semester = semester,
                        section = section,
                        day = day,
                        hour = hour
                    });
                }

                // Try to reschedule backup class rows (if any) by running GA, now including the current timetable constraints!
                var unscheduledClassRows = new List<object>();
                if (backupClassRows.Count > 0)
                {
                    foreach (var entry in backupClassRows)
                    {
                        // Build a 'subject' for GA
                        var backupSubject = new TimetableGA.TimetableEngine.Subject
                        {
                            SubjectCode = (string)entry.GetType().GetProperty("subject_code").GetValue(entry),
                            SubjectName = (string)entry.GetType().GetProperty("subject_name").GetValue(entry),
                            Credit = 1,
                            StaffAssigned = (string)entry.GetType().GetProperty("staff_code").GetValue(entry),
                            SubjectType = "Theory"
                        };

                        var singleSubList = new List<TimetableGA.TimetableEngine.Subject> { backupSubject };
                        var singleStaffAvail = await LoadAvailabilityForStaff(backupSubject.StaffAssigned, conn);

                        var ga = new TimetableGA.TimetableEngine();
                        ga.Initialize(singleSubList, singleStaffAvail, new Dictionary<string, Dictionary<string, HashSet<int>>>());
                        var (tt, conflicts2, chrom) = ga.GenerateGA();

                        if (conflicts2.Count == 0)
                        {
                            foreach (var slot in tt)
                            {
                                foreach (var kv in slot.HourlySlots)
                                {
                                    if (kv.Value == "---") continue;
                                    var cmd2 = new NpgsqlCommand(@"
                                             INSERT INTO classtimetable
                                             (staff_name, staff_code, department_id, year, semester, section, day, hour, subject_code, subject_name)
                                             VALUES
                                             (@staff_name, @staff_code, @department_id, @year, @semester, @section, @day, @hour, @subject_code, @subject_name);", conn);
                                    cmd2.Parameters.AddWithValue("@staff_name", entry.GetType().GetProperty("staff_name").GetValue(entry));
                                    cmd2.Parameters.AddWithValue("@staff_code", entry.GetType().GetProperty("staff_code").GetValue(entry));
                                    cmd2.Parameters.AddWithValue("@department_id", entry.GetType().GetProperty("department_id").GetValue(entry));
                                    cmd2.Parameters.AddWithValue("@year", entry.GetType().GetProperty("year").GetValue(entry));
                                    cmd2.Parameters.AddWithValue("@semester", entry.GetType().GetProperty("semester").GetValue(entry));
                                    cmd2.Parameters.AddWithValue("@section", entry.GetType().GetProperty("section").GetValue(entry));
                                    cmd2.Parameters.AddWithValue("@day", slot.Day);
                                    cmd2.Parameters.AddWithValue("@hour", kv.Key);
                                    cmd2.Parameters.AddWithValue("@subject_code", backupSubject.SubjectCode);
                                    cmd2.Parameters.AddWithValue("@subject_name", backupSubject.SubjectName);
                                    await cmd2.ExecuteNonQueryAsync();
                                }
                            }
                        }
                        else
                        {
                            unscheduledClassRows.Add(entry);
                        }
                    }
                }

                // Optionally, reschedule lab rows if required (pattern would match above)

                // Optionally, update status in pendingtimetabledata (this is a GET-only thing, you can keep or remove)
                var updateQuery = @"
             UPDATE pendingtimetabledata
             SET status = 'generated'
             WHERE LOWER(department) = LOWER(@toDepartment)
               AND LOWER(year) = LOWER(@year)
               AND LOWER(sem) = LOWER(@semester)
               AND LOWER(section) = LOWER(@section)
               AND TRIM(staffname) IS NOT NULL AND TRIM(staffname) <> ''";

                using var updateCmd = new NpgsqlCommand(updateQuery, conn);
                updateCmd.Parameters.AddWithValue("@toDepartment", toDepartment.Trim());
                updateCmd.Parameters.AddWithValue("@year", year.Trim());
                updateCmd.Parameters.AddWithValue("@semester", semester.Trim());
                updateCmd.Parameters.AddWithValue("@section", section.Trim());

                await updateCmd.ExecuteNonQueryAsync();

                return Ok(new
                {
                    message = conflicts.Count == 0 && unscheduledClassRows.Count == 0
                        ? "✅ Hybrid Timetable generated and stored successfully; all conflicts resolved."
                        : "⚠ Hybrid Timetable generated with some unsolved/conflicting cases. Some original class/lab slots could not be rescheduled.",
                    timetable,
                    labTimetable,
                    unresolvedClassConflicts = unscheduledClassRows,
                    // unresolvedLabConflicts = unscheduledLabRows, // implement if needed
                    usedLabIds = subjects.Where(s => !string.IsNullOrEmpty(s.LabId)).Select(s => s.LabId).Distinct().ToList(),
                    receivedPayload = new { toDepartment, year, semester, section },
                    conflicts = conflicts.Select(c => new
                    {
                        subject = c.Subject?.SubjectCode,
                        staff = c.Subject?.StaffAssigned,
                        reason = c.Reason
                    })
                });
            }
            catch (Exception ex)
            {
                return StatusCode(500, new
                {
                    message = "❌ Internal Server Error while generating timetable.",
                    error = ex.Message
                });
            }
        }

        //     // Helper function to load staff availability from DB
        private async Task<Dictionary<string, Dictionary<string, HashSet<int>>>> LoadAvailabilityForStaff(string staffCode, NpgsqlConnection conn)
        {
            var result = new Dictionary<string, Dictionary<string, HashSet<int>>>
            {
                [staffCode] = new Dictionary<string, HashSet<int>>()
            };
            foreach (var d in new[] { "Mon", "Tue", "Wed", "Thu", "Fri" })
                result[staffCode][d] = new();

            var cmd = new NpgsqlCommand(
                @"SELECT day, hour FROM classtimetable WHERE staff_code=@staff", conn);
            cmd.Parameters.AddWithValue("@staff", staffCode);
            using var rdr = await cmd.ExecuteReaderAsync();
            while (await rdr.ReadAsync())
            {
                string day = rdr["day"].ToString();
                int hour = Convert.ToInt32(rdr["hour"]);
                result[staffCode][day].Add(hour);
            }
            rdr.Close();
            return result;
        }
    
        private string GenerateResponseMessage(List<TimetableGA.TimetableEngine.Conflict> conflicts,
            List<object> unscheduledClasses, List<object> unscheduledLabs, List<object> conflictDetails)
        {
            if (conflicts.Count == 0 && unscheduledClasses.Count == 0 && unscheduledLabs.Count == 0)
            {
                return "✅ Enhanced Hybrid Timetable generated successfully with complete conflict resolution!";
            }
            else if (conflicts.Count == 0)
            {
                return $"⚠️ Timetable generated with {unscheduledClasses.Count + unscheduledLabs.Count} items requiring manual scheduling.";
            }
            else
            {
                return $"⚠️ Timetable generated with {conflicts.Count} algorithmic conflicts and {conflictDetails.Count} database conflicts partially resolved.";
            }
        }

        private async Task<Dictionary<string, Dictionary<string, HashSet<int>>>> LoadCurrentAvailabilityForStaff(
            string staffCode, NpgsqlConnection conn, NpgsqlTransaction transaction)
        {
            var availability = new Dictionary<string, Dictionary<string, HashSet<int>>>();
            availability[staffCode] = new();
            foreach (var day in new[] { "Mon", "Tue", "Wed", "Thu", "Fri" })
                availability[staffCode][day] = new();

            var query = "SELECT day, hour FROM classtimetable WHERE staff_code = @staff_code";
            using var cmd = new NpgsqlCommand(query, conn, transaction);
            cmd.Parameters.AddWithValue("@staff_code", staffCode);

            using var reader = await cmd.ExecuteReaderAsync();
            while (await reader.ReadAsync())
            {
                var day = reader["day"].ToString();
                var hour = Convert.ToInt32(reader["hour"]);
                availability[staffCode][day].Add(hour);
            }

            return availability;
        }
    

        private string GenerateResponseMessage1(List<TimetableGA.TimetableEngine.Conflict> conflicts,
            List<object> unscheduledClasses, List<object> unscheduledLabs, List<object> conflictDetails)
        {
            if (conflicts.Count == 0 && unscheduledClasses.Count == 0 && unscheduledLabs.Count == 0)
            {
                return "✅ Enhanced Hybrid Timetable generated successfully with complete conflict resolution!";
            }
            else if (conflicts.Count == 0)
            {
                return $"⚠️ Timetable generated with {unscheduledClasses.Count + unscheduledLabs.Count} items requiring manual scheduling.";
            }
            else
            {
                return $"⚠️ Timetable generated with {conflicts.Count} algorithmic conflicts and {conflictDetails.Count} database conflicts partially resolved.";
            }
        }

        private async Task<Dictionary<string, Dictionary<string, HashSet<int>>>> LoadCurrentAvailabilityForStaff1(
            string staffCode, NpgsqlConnection conn, NpgsqlTransaction transaction)
        {
            var availability = new Dictionary<string, Dictionary<string, HashSet<int>>>();
            availability[staffCode] = new();
            foreach (var day in new[] { "Mon", "Tue", "Wed", "Thu", "Fri" })
                availability[staffCode][day] = new();

            var query = "SELECT day, hour FROM classtimetable WHERE staff_code = @staff_code";
            using var cmd = new NpgsqlCommand(query, conn, transaction);
            cmd.Parameters.AddWithValue("@staff_code", staffCode);

            using var reader = await cmd.ExecuteReaderAsync();
            while (await reader.ReadAsync())
            {
                var day = reader["day"].ToString();
                var hour = Convert.ToInt32(reader["hour"]);
                availability[staffCode][day].Add(hour);
            }

            return availability;
        }
        public class TimetableRequest
        {
            [JsonPropertyName("department")]
            public string Department { get; set; }

            [JsonPropertyName("year")]
            public string Year { get; set; }

            [JsonPropertyName("semester")]
            public string Semester { get; set; }

            [JsonPropertyName("section")]
            public string Section { get; set; }

            [JsonPropertyName("subjects")]
            public List<SubjectDto> Subjects { get; set; }
        }

        public class SubjectDto
        {
            [JsonPropertyName("subjectCode")]
            public string SubjectCode { get; set; }

            [JsonPropertyName("subjectName")]
            public string SubjectName { get; set; }

            [JsonPropertyName("subjectType")]
            public string SubjectType { get; set; }

            [JsonPropertyName("credit")]
            public int Credit { get; set; }

            [JsonPropertyName("staffAssigned")]
            public string StaffAssigned { get; set; }

            [JsonPropertyName("labId")]
            public string LabId { get; set; }
        }




        public class StaffDto
        {
            public string? department { get; set; }
            public string? department_id { get; set; }
            public string? block { get; set; }
            public string staffId { get; set; }
            public string name { get; set; }
            public string? subject1 { get; set; }
            public string? subject2 { get; set; }
            public string? subject3 { get; set; }
        }





    }
}
