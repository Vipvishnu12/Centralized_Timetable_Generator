using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Configuration;
using Npgsql;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

namespace Timetablegenerator.Controllers
{
    [ApiController]
    [Route("api/[controller]")]
    public class TimetableController : ControllerBase
    {
        private readonly IConfiguration _configuration;
        public TimetableController(IConfiguration configuration)
        {
            _configuration = configuration;
        }

        // DTO Classes
        public class TimetableRequest
        {
            public string Department { get; set; }
            public string Year { get; set; }
            public string Semester { get; set; }
            public string Section { get; set; }
            public int TotalHoursPerDay { get; set; } = 7;
            public List<SubjectDto> Subjects { get; set; }
        }

        public class SubjectDto
        {
            public string SubjectCode { get; set; }
            public string SubjectName { get; set; }
            public string SubjectType { get; set; }
            public int Credit { get; set; }
            public string StaffAssigned { get; set; }
            public string LabId { get; set; }
        }

        // Helper class for per-task scheduling
        public class TaskUnit
        {
            public string SubjectCode;
            public string SubjectName;
            public string StaffAssigned;
            public string LabId;
            public bool IsLab;
            public int Duration;
            public string Kind;
            public string Day;
            public int StartHour;
            public List<(string day, int hour)> Tried = new();
        }

        [HttpPost("generateCrossDepartmentTimetableBacktracking")]
        public async Task<IActionResult> GenerateCrossDepartmentTimetableBacktracking([FromBody] TimetableRequest request)
        {
            var cs = _configuration.GetConnectionString("DefaultConnection");
            try
            {
                string[] DAYS = new[] { "Mon", "Tue", "Wed", "Thu", "Fri" };
                int HOURS = Math.Max(1, request.TotalHoursPerDay);

                // Staff code parsing helper
                (string staffName, string staffCode) SplitStaff(string staffAssigned)
                {
                    if (string.IsNullOrWhiteSpace(staffAssigned)) return ("---", "---");
                    var name = staffAssigned;
                    var code = staffAssigned;
                    if (staffAssigned.Contains("("))
                    {
                        var parts = staffAssigned.Split('(', StringSplitOptions.TrimEntries);
                        name = parts[0].Trim();
                        code = parts.Length > 1 ? parts[1].Replace(")", "").Trim() : "---";
                    }
                    return (name, code);
                }

                // Build subjects from request, ignore those without staff assigned
                var subjects = new List<(string code, string name, string type, int credit, string staff, string labId)>();
                foreach (var s in request.Subjects ?? Enumerable.Empty<SubjectDto>())
                {
                    if (string.IsNullOrWhiteSpace(s.StaffAssigned)) continue;
                    var type = (s.SubjectType ?? "theory").Trim().ToLower();
                    subjects.Add((
                        s.SubjectCode ?? "---",
                        s.SubjectName ?? "---",
                        type,
                        s.Credit,
                        s.StaffAssigned,
                        (type == "lab" || type == "embedded") ? (s.LabId?.Trim()) : null
                    ));
                }
                if (subjects.Count == 0)
                    return BadRequest(new { message = "❌ No valid subjects found (missing staff)." });

                // Load existing DB occupancy maps for staff and labs
                var staffOcc = new Dictionary<string, Dictionary<string, HashSet<int>>>();
                var labOcc = new Dictionary<string, Dictionary<string, HashSet<int>>>();

                using var conn = new NpgsqlConnection(cs);
                await conn.OpenAsync();

                void EnsureDayMap<TKey>(Dictionary<TKey, Dictionary<string, HashSet<int>>> map, TKey key)
                {
                    if (!map.ContainsKey(key))
                        map[key] = DAYS.ToDictionary(d => d, d => new HashSet<int>());
                }

                // Load staff occupancy from DB
                using (var cmd = new NpgsqlCommand("SELECT staff_code, day, hour FROM classtimetable", conn))
                using (var rd = await cmd.ExecuteReaderAsync())
                {
                    while (await rd.ReadAsync())
                    {
                        var sc = rd["staff_code"]?.ToString() ?? "---";
                        var day = rd["day"]?.ToString() ?? "Mon";
                        var hr = Convert.ToInt32(rd["hour"]);
                        EnsureDayMap(staffOcc, sc);
                        if (!staffOcc[sc].ContainsKey(day)) staffOcc[sc][day] = new HashSet<int>();
                        staffOcc[sc][day].Add(hr);
                    }
                }

                // Load lab occupancy from DB
                using (var cmd = new NpgsqlCommand("SELECT lab_id, day, hour FROM labtimetable", conn))
                using (var rd = await cmd.ExecuteReaderAsync())
                {
                    while (await rd.ReadAsync())
                    {
                        var lab = rd["lab_id"]?.ToString() ?? "---";
                        var day = rd["day"]?.ToString() ?? "Mon";
                        var hr = Convert.ToInt32(rd["hour"]);
                        EnsureDayMap(labOcc, lab);
                        if (!labOcc[lab].ContainsKey(day)) labOcc[lab][day] = new HashSet<int>();
                        labOcc[lab][day].Add(hr);
                    }
                }

                // Ensure days map for all staff and labs
                foreach (var s in subjects)
                {
                    var (_, staffCode) = SplitStaff(s.staff);
                    EnsureDayMap(staffOcc, staffCode);
                    if (!string.IsNullOrEmpty(s.labId)) EnsureDayMap(labOcc, s.labId);
                }

                // Translate subject info into schedule-able tasks with special constraints:
                var tasks = new List<TaskUnit>();

                // Allowed continuous blocks for labs
                var labAllowedBlocks = new List<int[]> { new[] { 1, 2, 3, 4 }, new[] { 4, 5, 6, 7 } };

                // Allowed continuous 2-hour blocks for embedded labs
                var embeddedLabAllowedBlocks = new List<int[]> {
                    new[] { 1, 2 },
                    new[] { 2, 3 },
                    new[] { 3, 4 },
                    new[] { 5, 6 },
                    new[] { 6, 7 }
                };

                // For embedded theory sessions - non-adjacent single hours (to be randomized in placement)

                foreach (var s in subjects)
                {
                    switch (s.type)
                    {
                        case "lab":
                            // One 4-hour continuous block task representing the lab
                            tasks.Add(new TaskUnit
                            {
                                SubjectCode = s.code,
                                SubjectName = s.name,
                                StaffAssigned = s.staff,
                                LabId = s.labId,
                                IsLab = true,
                                Duration = 4,
                                Kind = "LAB4"
                            });
                            break;

                        case "embedded":
                            // Embedded lab 2-hour continuous block task
                            tasks.Add(new TaskUnit
                            {
                                SubjectCode = s.code,
                                SubjectName = s.name,
                                StaffAssigned = s.staff,
                                LabId = s.labId,
                                IsLab = true,
                                Duration = 2,
                                Kind = "EMB_LAB2"
                            });

                            // Two embedded theory 1-hour tasks non-adjacent, randomized placement
                            tasks.Add(new TaskUnit
                            {
                                SubjectCode = s.code,
                                SubjectName = s.name,
                                StaffAssigned = s.staff,
                                LabId = null,
                                IsLab = false,
                                Duration = 1,
                                Kind = "EMB_TH1"
                            });
                            tasks.Add(new TaskUnit
                            {
                                SubjectCode = s.code,
                                SubjectName = s.name,
                                StaffAssigned = s.staff,
                                LabId = null,
                                IsLab = false,
                                Duration = 1,
                                Kind = "EMB_TH1"
                            });
                            break;

                        default: // theory and any other
                            // Credit count equals number of 1-hour theory tasks
                            int count = Math.Max(0, s.credit);
                            for (int i = 0; i < count; i++)
                            {
                                tasks.Add(new TaskUnit
                                {
                                    SubjectCode = s.code,
                                    SubjectName = s.name,
                                    StaffAssigned = s.staff,
                                    LabId = null,
                                    IsLab = false,
                                    Duration = 1,
                                    Kind = "TH1"
                                });
                            }
                            break;
                    }
                }

                // Initialize timetable grid: Dictionary of {Day -> {Hour -> slot string}}
                var timetable = DAYS.Select(d => new
                {
                    Day = d,
                    Slots = Enumerable.Range(1, HOURS).ToDictionary(h => h, _ => "---")
                }).ToList();

                // Helpers for grid access and checking

                bool IsFreeInGrid(string day, int start, int duration)
                {
                    var row = timetable.First(t => t.Day == day).Slots;
                    for (int h = start; h < start + duration; h++)
                        if (h < 1 || h > HOURS || row[h] != "---") return false;
                    return true;
                }

                void PlaceInGrid(string subjectCode, string staffAssigned, string day, int start, int duration)
                {
                    var row = timetable.First(t => t.Day == day).Slots;
                    for (int h = start; h < start + duration; h++)
                        row[h] = $"{subjectCode} ({staffAssigned})";
                }

                void RemoveFromGrid(string day, int start, int duration)
                {
                    var row = timetable.First(t => t.Day == day).Slots;
                    for (int h = start; h < start + duration; h++)
                        row[h] = "---";
                }

                // Order tasks: labs first (long continuous blocks), then embedded labs, embedded theory, theory
                tasks = tasks
                    .OrderByDescending(t => t.Kind == "LAB4" ? 3 :
                                           t.Kind == "EMB_LAB2" ? 2 :
                                           t.Kind == "EMB_TH1" ? 1 :
                                           0)
                    .ThenByDescending(t => t.Duration)
                    .ToList();

                // State to ensure embedded theory non-adjacency for same subject
                var embTheoryPlaced = new Dictionary<string, List<(string day, int hour)>>();

                bool CanPlaceEmbeddedTheory(TaskUnit t, string day, int hour)
                {
                    if (!embTheoryPlaced.ContainsKey(t.SubjectCode)) return true;
                    foreach (var (d, h) in embTheoryPlaced[t.SubjectCode])
                    {
                        // Must not place at same day adjacent hours (hour-1, hour, hour+1)
                        if (d == day && (h == hour || h == hour - 1 || h == hour + 1))
                            return false;
                    }
                    return true;
                }

                // Custom Fits function enforcing all constraints, continuous blocks, and lab/lab use exclusion
                bool Fits(TaskUnit t, string day, int start)
                {
                    // Check grid free
                    if (!IsFreeInGrid(day, start, t.Duration)) return false;

                    var (_, staffCode) = SplitStaff(t.StaffAssigned);

                    // Check staff occupancy
                    for (int h = start; h < start + t.Duration; h++)
                    {
                        if (staffOcc.TryGetValue(staffCode, out var dm) && dm.TryGetValue(day, out var set) && set.Contains(h))
                            return false;
                    }

                    // Labs: lab occupancy + continuous block allowed only for specific hour ranges
                    if (t.IsLab)
                    {
                        if (!string.IsNullOrEmpty(t.LabId))
                        {
                            for (int h = start; h < start + t.Duration; h++)
                            {
                                if (labOcc.TryGetValue(t.LabId, out var dm) && dm.TryGetValue(day, out var set) && set.Contains(h))
                                    return false;
                            }
                        }

                        // Continuous block constraints:
                        if (t.Kind == "LAB4")
                        {
                            // Only allow 4-contiguous blocks in allowed sets
                            if (!labAllowedBlocks.Any(block => block[0] == start && block.Length == t.Duration))
                                return false;
                        }
                        if (t.Kind == "EMB_LAB2")
                        {
                            // Only allow 2-contiguous blocks in embedded allowed sets
                            if (!embeddedLabAllowedBlocks.Any(block => block[0] == start && block.Length == t.Duration))
                                return false;
                        }
                    }

                    // Embedded theory: non-adjacent check
                    if (t.Kind == "EMB_TH1" && !CanPlaceEmbeddedTheory(t, day, start))
                        return false;

                    return true;
                }

                void Commit(TaskUnit t, string day, int start)
                {
                    t.Day = day;
                    t.StartHour = start;
                    var (_, staffCode) = SplitStaff(t.StaffAssigned);

                    PlaceInGrid(t.SubjectCode, t.StaffAssigned, day, start, t.Duration);

                    for (int h = start; h < start + t.Duration; h++)
                        staffOcc[staffCode][day].Add(h);

                    if (t.IsLab && !string.IsNullOrEmpty(t.LabId))
                    {
                        for (int h = start; h < start + t.Duration; h++)
                            labOcc[t.LabId][day].Add(h);
                    }

                    if (t.Kind == "EMB_TH1")
                    {
                        if (!embTheoryPlaced.ContainsKey(t.SubjectCode))
                            embTheoryPlaced[t.SubjectCode] = new List<(string, int)>();
                        embTheoryPlaced[t.SubjectCode].Add((day, start));
                    }
                }

                void Revert(TaskUnit t)
                {
                    var (_, staffCode) = SplitStaff(t.StaffAssigned);

                    RemoveFromGrid(t.Day, t.StartHour, t.Duration);

                    for (int h = t.StartHour; h < t.StartHour + t.Duration; h++)
                        staffOcc[staffCode][t.Day].Remove(h);

                    if (t.IsLab && !string.IsNullOrEmpty(t.LabId))
                    {
                        for (int h = t.StartHour; h < t.StartHour + t.Duration; h++)
                            labOcc[t.LabId][t.Day].Remove(h);
                    }

                    if (t.Kind == "EMB_TH1" && embTheoryPlaced.ContainsKey(t.SubjectCode))
                    {
                        embTheoryPlaced[t.SubjectCode].RemoveAll(x => x.day == t.Day && x.hour == t.StartHour);
                    }

                    t.Tried.Add((t.Day, t.StartHour));
                    t.Day = null;
                    t.StartHour = 0;
                }

                // Candidate slots generator with randomization for theory and embedded theory to spread random selections
                IEnumerable<(string day, int start)> Candidates(TaskUnit t)
                {
                    var daysShuffled = DAYS.OrderBy(_ => Guid.NewGuid()).ToArray();
                    var hours = Enumerable.Range(1, HOURS - t.Duration + 1).OrderBy(_ => Guid.NewGuid()).ToArray();

                    foreach (var d in daysShuffled)
                        foreach (var h in hours)
                            if (!t.Tried.Contains((d, h)))
                                yield return (d, h);
                }

                // Backtracking solver with conflict popping and retrying, stops and suggests manual fix if stuck
                bool Solve(int idx)
                {
                    if (idx == tasks.Count) return true;

                    var t = tasks[idx];

                    foreach (var (day, start) in Candidates(t))
                    {
                        if (!Fits(t, day, start)) continue;

                        Commit(t, day, start);

                        if (Solve(idx + 1)) return true;

                        Revert(t);
                    }

                    // No valid position found for this task -> must stop and ask user correction
                    return false;
                }

                var ok = Solve(0);

                if (!ok)
                {
                    // Return partial timetable with message to manually fix conflicts before retry
                    return Ok(new
                    {
                        message = "⚠ Backtracking failed to place all tasks under given constraints due to conflicts with existing timetable. Please adjust inputs manually.",
                        timetable,
                        usedLabIds = tasks.Where(x => x.IsLab && !string.IsNullOrEmpty(x.LabId)).Select(x => x.LabId).Distinct().ToList(),
                        receivedPayload = request
                    });
                }

                // Write final timetable to DB
                foreach (var t in tasks)
                {
                    var (staffName, staffCode) = SplitStaff(t.StaffAssigned);

                    // Insert all hours into classtimetable
                    for (int h = t.StartHour; h < t.StartHour + t.Duration; h++)
                    {
                        using (var icClass = new NpgsqlCommand(@"
            INSERT INTO classtimetable
            (staff_name, staff_code, department_id, year, semester, section, day, hour, subject_code, subject_name)
            VALUES
            (@staff_name, @staff_code, @department, @year, @semester, @section, @day, @hour, @subject_code, @subject_name);", conn))
                        {
                            icClass.Parameters.AddWithValue("@staff_name", staffName ?? "---");
                            icClass.Parameters.AddWithValue("@staff_code", staffCode ?? "---");
                            icClass.Parameters.AddWithValue("@department", request.Department ?? "---");
                            icClass.Parameters.AddWithValue("@year", request.Year ?? "---");
                            icClass.Parameters.AddWithValue("@semester", request.Semester ?? "---");
                            icClass.Parameters.AddWithValue("@section", request.Section ?? "---");
                            icClass.Parameters.AddWithValue("@day", t.Day ?? "---");
                            icClass.Parameters.AddWithValue("@hour", h);
                            icClass.Parameters.AddWithValue("@subject_code", t.SubjectCode ?? "---");
                            icClass.Parameters.AddWithValue("@subject_name", t.SubjectName ?? "---");
                            await icClass.ExecuteNonQueryAsync();
                        }
                    }

                    // Insert lab timetable data with special logic
                    if (t.IsLab && !string.IsNullOrEmpty(t.LabId))
                    {
                        // If lab starts at 4th hour, skip hour 4 for labtimetable
                        if (t.StartHour == 4)
                        {
                            for (int h = 5; h < 4 + t.Duration; h++) // hours 5,6,7 only
                            {
                                using (var icLab = new NpgsqlCommand(@"
                    INSERT INTO labtimetable
                    (lab_id, subject_code, subject_name, staff_name, department, year, semester, section, day, hour)
                    VALUES
                    (@lab_id, @subject_code, @subject_name, @staff_name, @department, @year, @semester, @section, @day, @hour);", conn))
                                {
                                    icLab.Parameters.AddWithValue("@lab_id", t.LabId ?? (object)DBNull.Value);
                                    icLab.Parameters.AddWithValue("@subject_code", t.SubjectCode ?? "---");
                                    icLab.Parameters.AddWithValue("@subject_name", t.SubjectName ?? "---");
                                    icLab.Parameters.AddWithValue("@staff_name", staffName ?? "---");
                                    icLab.Parameters.AddWithValue("@department", request.Department ?? "---");
                                    icLab.Parameters.AddWithValue("@year", request.Year ?? "---");
                                    icLab.Parameters.AddWithValue("@semester", request.Semester ?? "---");
                                    icLab.Parameters.AddWithValue("@section", request.Section ?? "---");
                                    icLab.Parameters.AddWithValue("@day", t.Day ?? "---");
                                    icLab.Parameters.AddWithValue("@hour", h);
                                    await icLab.ExecuteNonQueryAsync();
                                }
                            }
                        }
                        else
                        {
                            // Normal insertion for all lab hours
                            for (int h = t.StartHour; h < t.StartHour + t.Duration; h++)
                            {
                                using (var icLab = new NpgsqlCommand(@"
                    INSERT INTO labtimetable
                    (lab_id, subject_code, subject_name, staff_name, department, year, semester, section, day, hour)
                    VALUES
                    (@lab_id, @subject_code, @subject_name, @staff_name, @department, @year, @semester, @section, @day, @hour);", conn))
                                {
                                    icLab.Parameters.AddWithValue("@lab_id", t.LabId ?? (object)DBNull.Value);
                                    icLab.Parameters.AddWithValue("@subject_code", t.SubjectCode ?? "---");
                                    icLab.Parameters.AddWithValue("@subject_name", t.SubjectName ?? "---");
                                    icLab.Parameters.AddWithValue("@staff_name", staffName ?? "---");
                                    icLab.Parameters.AddWithValue("@department", request.Department ?? "---");
                                    icLab.Parameters.AddWithValue("@year", request.Year ?? "---");
                                    icLab.Parameters.AddWithValue("@semester", request.Semester ?? "---");
                                    icLab.Parameters.AddWithValue("@section", request.Section ?? "---");
                                    icLab.Parameters.AddWithValue("@day", t.Day ?? "---");
                                    icLab.Parameters.AddWithValue("@hour", h);
                                    await icLab.ExecuteNonQueryAsync();
                                }
                            }
                        }
                    }
                }

                var responseTimetable = timetable.Select(row => new
                {
                    Day = row.Day,
                    HourlySlots = row.Slots
                }).ToList();

                return Ok(new
                {
                    message = "✅ Timetable generated with constrained backtracking (Labs continuous 4h in fixed blocks, embedded 2h continuous + 2 random theory), respecting existing timetable conflicts.",
                    timetable = responseTimetable,
                    usedLabIds = tasks.Where(x => x.IsLab && !string.IsNullOrEmpty(x.LabId)).Select(x => x.LabId).Distinct().ToList(),
                    receivedPayload = request
                });
            }
            catch (Exception ex)
            {
                return StatusCode(500, new { message = "❌ Internal Server Error while generating timetable.", error = ex.Message });
            }
        }
    }
}
//using Microsoft.AspNetCore.Mvc;
//using Microsoft.Extensions.Configuration;
//using Npgsql;
//using System;
//using System.Collections.Generic;
//using System.Linq;
//using System.Threading.Tasks;

//namespace Timetablegenerator.Controllers
//{
//    [ApiController]
//    [Route("api/[controller]")]
//    public class TimetableController : ControllerBase
//    {
//        private readonly IConfiguration _configuration;

//        // Static readonly fields for allowed blocks
//        private static readonly List<int[]> LabAllowedBlocks = new List<int[]> { new[] { 1, 2, 3, 4 }, new[] { 4, 5, 6, 7 } };
//        private static readonly List<int[]> EmbeddedLabAllowedBlocks = new List<int[]> {
//            new[] { 1, 2 }, new[] { 2, 3 }, new[] { 3, 4 }, new[] { 5, 6 }, new[] { 6, 7 }
//        };

//        public TimetableController(IConfiguration configuration)
//        {
//            _configuration = configuration;
//        }

//        // DTO Classes
//        public class TimetableRequest
//        {
//            public string Department { get; set; }
//            public string Year { get; set; }
//            public string Semester { get; set; }
//            public string Section { get; set; }
//            public int TotalHoursPerDay { get; set; } = 7;
//            public List<SubjectDto> Subjects { get; set; }
//        }
//        public class SubjectDto
//        {
//            public string SubjectCode { get; set; }
//            public string SubjectName { get; set; }
//            public string SubjectType { get; set; }
//            public int Credit { get; set; }
//            public string StaffAssigned { get; set; }
//            public string LabId { get; set; }
//        }

//        // Internal model
//        public class TaskUnit
//        {
//            public string SubjectCode;
//            public string SubjectName;
//            public string StaffAssigned;
//            public string LabId;
//            public bool IsLab;
//            public int Duration;
//            public string Kind;
//            public string Day;
//            public int StartHour;
//            public List<(string day, int hour)> Tried = new();
//        }

//        /// <summary>
//        /// Get DB tasks that conflict with a given slot
//        /// </summary>
//        private async Task<List<TaskUnit>> GetConflictingDbTasks(NpgsqlConnection conn, string reason, string conflictingId, string day, int hour, NpgsqlTransaction tx)
//        {
//            var result = new List<TaskUnit>();
//            string sql;
//            if (reason == "staff")
//            {
//                sql = @"SELECT staff_name, staff_code, subject_code, subject_name, department_id, year, semester, section, day, hour
//                        FROM classtimetable
//                        WHERE staff_code = @id AND day=@day AND hour=@hour
//                        FOR UPDATE;";
//            }
//            else if (reason == "lab")
//            {
//                sql = @"SELECT lab_id, subject_code, subject_name, staff_name, department, year, semester, section, day, hour
//                        FROM labtimetable
//                        WHERE lab_id = @id AND day=@day AND hour=@hour
//                        FOR UPDATE;";
//            }
//            else return result;
//            using (var cmd = new NpgsqlCommand(sql, conn, tx))
//            {
//                cmd.Parameters.AddWithValue("@id", conflictingId);
//                cmd.Parameters.AddWithValue("@day", day);
//                cmd.Parameters.AddWithValue("@hour", hour);
//                using var rd = await cmd.ExecuteReaderAsync();
//                var rows = new List<(string subj, string name, string staff, string lab, int hr, string d)>();
//                while (await rd.ReadAsync())
//                {
//                    rows.Add((
//                        rd["subject_code"].ToString(),
//                        rd["subject_name"].ToString(),
//                        reason == "staff" ? rd["staff_code"].ToString() : rd["staff_name"].ToString(),
//                        reason == "lab" ? rd["lab_id"].ToString() : null,
//                        Convert.ToInt32(rd["hour"]),
//                        rd["day"].ToString()
//                    ));
//                }
//                rd.Close();
//                // Group contiguous rows into TaskUnits
//                foreach (var group in rows.GroupBy(x => (x.subj, x.name, x.staff, x.lab, x.d)))
//                {
//                    var ordered = group.OrderBy(x => x.hr).ToList();
//                    int start = ordered[0].hr;
//                    int last = start;
//                    foreach (var r in ordered.Skip(1))
//                    {
//                        if (r.hr != last + 1)
//                        {
//                            // end segment
//                            result.Add(new TaskUnit
//                            {
//                                SubjectCode = group.Key.subj,
//                                SubjectName = group.Key.name,
//                                StaffAssigned = group.Key.staff,
//                                LabId = group.Key.lab,
//                                IsLab = !string.IsNullOrEmpty(group.Key.lab),
//                                Duration = last - start + 1,
//                                Kind = !string.IsNullOrEmpty(group.Key.lab) ? "LAB" : "TH1",
//                                Day = group.Key.d,
//                                StartHour = start
//                            });
//                            start = r.hr;
//                        }
//                        last = r.hr;
//                    }
//                    // Add last segment
//                    result.Add(new TaskUnit
//                    {
//                        SubjectCode = group.Key.subj,
//                        SubjectName = group.Key.name,
//                        StaffAssigned = group.Key.staff,
//                        LabId = group.Key.lab,
//                        IsLab = !string.IsNullOrEmpty(group.Key.lab),
//                        Duration = last - start + 1,
//                        Kind = !string.IsNullOrEmpty(group.Key.lab) ? "LAB" : "TH1",
//                        Day = group.Key.d,
//                        StartHour = start
//                    });
//                }
//            }
//            return result;
//        }

//        /// <summary>
//        /// Attempt to repair a conflict locally by rescheduling conflicting DB tasks
//        /// </summary>
//        private async Task<bool> RepairConflictingDbTasks(
//            NpgsqlConnection conn,
//            NpgsqlTransaction tx,
//            Dictionary<string, Dictionary<string, HashSet<int>>> staffOcc,
//            Dictionary<string, Dictionary<string, HashSet<int>>> labOcc,
//            List<dynamic> timetable,
//            string reason,
//            string conflictingId,
//            string day,
//            int hour,
//            Func<TaskUnit, string, int, bool> Fits,
//            Action<TaskUnit, string, int> Commit,
//            Action<TaskUnit> Revert)
//        {
//            var conflicts = await GetConflictingDbTasks(conn, reason, conflictingId, day, hour, tx);
//            if (!conflicts.Any() || conflicts.Count > 6) return false;

//            // Temporarily remove them from occupancy + grid
//            foreach (var t in conflicts)
//            {
//                for (int h = t.StartHour; h < t.StartHour + t.Duration; h++)
//                    staffOcc[t.StaffAssigned][t.Day].Remove(h);
//                if (t.IsLab)
//                {
//                    for (int h = t.StartHour; h < t.StartHour + t.Duration; h++)
//                        labOcc[t.LabId][t.Day].Remove(h);
//                }
//                var row = timetable.First(x => x.Day == t.Day).Slots;
//                for (int h = t.StartHour; h < t.StartHour + t.Duration; h++)
//                    row[h] = "---";
//                t.Day = null;
//                t.StartHour = 0;
//            }

//            int attempts = 0;
//            bool backtrack(int idx)
//            {
//                if (idx == conflicts.Count) return true;
//                var t = conflicts[idx];
//                foreach (var (d, s) in CandidatesLocal(timetable, t))
//                {
//                    if (attempts++ > 1000) // Cap attempts to avoid runaway CPU
//                        return false;
//                    if (!Fits(t, d, s)) { t.Tried.Add((d, s)); continue; }
//                    Commit(t, d, s);
//                    if (backtrack(idx + 1)) return true;
//                    Revert(t);
//                }
//                return false;
//            }

//            bool ok = backtrack(0);

//            if (!ok)
//            {
//                // Restore occupancy/grid for conflicts (rollback in-memory changes)
//                foreach (var t in conflicts)
//                {
//                    if (string.IsNullOrEmpty(t.Day) || t.StartHour == 0) continue; // Not placed
//                    for (int h = t.StartHour; h < t.StartHour + t.Duration; h++)
//                        staffOcc[t.StaffAssigned][t.Day].Add(h);
//                    if (t.IsLab)
//                    {
//                        for (int h = t.StartHour; h < t.StartHour + t.Duration; h++)
//                            labOcc[t.LabId][t.Day].Add(h);
//                    }
//                    var row = timetable.First(x => x.Day == t.Day).Slots;
//                    for (int h = t.StartHour; h < t.StartHour + t.Duration; h++)
//                        row[h] = $"{t.SubjectCode} ({t.StaffAssigned})";
//                }
//                return false;
//            }

//            // If success, backup & update DB
//            foreach (var t in conflicts)
//            {
//                // Backup relevant rows before deletion
//                if (t.IsLab)
//                {
//                    using var b = new NpgsqlCommand(@"INSERT INTO labtimetable_backup SELECT * FROM labtimetable 
//                                                      WHERE lab_id=@id AND day=@day AND hour BETWEEN @s AND @e;", conn, tx);
//                    b.Parameters.AddWithValue("@id", t.LabId);
//                    b.Parameters.AddWithValue("@day", t.Day);
//                    b.Parameters.AddWithValue("@s", t.StartHour);
//                    b.Parameters.AddWithValue("@e", t.StartHour + t.Duration - 1);
//                    await b.ExecuteNonQueryAsync();

//                    using var d = new NpgsqlCommand(@"DELETE FROM labtimetable WHERE lab_id=@id AND day=@day AND hour BETWEEN @s AND @e;", conn, tx);
//                    d.Parameters.AddWithValue("@id", t.LabId);
//                    d.Parameters.AddWithValue("@day", t.Day);
//                    d.Parameters.AddWithValue("@s", t.StartHour);
//                    d.Parameters.AddWithValue("@e", t.StartHour + t.Duration - 1);
//                    await d.ExecuteNonQueryAsync();
//                }
//                else
//                {
//                    using var b = new NpgsqlCommand(@"INSERT INTO classtimetable_backup SELECT * FROM classtimetable 
//                                                      WHERE staff_code=@id AND day=@day AND hour BETWEEN @s AND @e;", conn, tx);
//                    b.Parameters.AddWithValue("@id", t.StaffAssigned);
//                    b.Parameters.AddWithValue("@day", t.Day);
//                    b.Parameters.AddWithValue("@s", t.StartHour);
//                    b.Parameters.AddWithValue("@e", t.StartHour + t.Duration - 1);
//                    await b.ExecuteNonQueryAsync();

//                    using var d = new NpgsqlCommand(@"DELETE FROM classtimetable WHERE staff_code=@id AND day=@day AND hour BETWEEN @s AND @e;", conn, tx);
//                    d.Parameters.AddWithValue("@id", t.StaffAssigned);
//                    d.Parameters.AddWithValue("@day", t.Day);
//                    d.Parameters.AddWithValue("@s", t.StartHour);
//                    d.Parameters.AddWithValue("@e", t.StartHour + t.Duration - 1);
//                    await d.ExecuteNonQueryAsync();
//                }
//                // Insert updated placement (new reschedule)
//                for (int h = t.StartHour; h < t.StartHour + t.Duration; h++)
//                {
//                    if (t.IsLab)
//                    {
//                        using var ins = new NpgsqlCommand(@"INSERT INTO labtimetable (lab_id, subject_code, subject_name, staff_name, department, year, semester, section, day, hour)
//                                                            VALUES (@lab,@sc,@sn,@st,'---','---','---','---',@day,@hr);", conn, tx);
//                        ins.Parameters.AddWithValue("@lab", t.LabId);
//                        ins.Parameters.AddWithValue("@sc", t.SubjectCode);
//                        ins.Parameters.AddWithValue("@sn", t.SubjectName);
//                        ins.Parameters.AddWithValue("@st", t.StaffAssigned);
//                        ins.Parameters.AddWithValue("@day", t.Day);
//                        ins.Parameters.AddWithValue("@hr", h);
//                        await ins.ExecuteNonQueryAsync();
//                    }
//                    else
//                    {
//                        using var ins = new NpgsqlCommand(@"INSERT INTO classtimetable (staff_name, staff_code, department_id, year, semester, section, day, hour, subject_code, subject_name)
//                                                            VALUES (@st,'---','---','---','---','---',@day,@hr,@sc,@sn);", conn, tx);
//                        ins.Parameters.AddWithValue("@st", t.StaffAssigned);
//                        ins.Parameters.AddWithValue("@day", t.Day);
//                        ins.Parameters.AddWithValue("@hr", h);
//                        ins.Parameters.AddWithValue("@sc", t.SubjectCode);
//                        ins.Parameters.AddWithValue("@sn", t.SubjectName);
//                        await ins.ExecuteNonQueryAsync();
//                    }
//                }
//            }

//            return true;
//        }

//        // Candidate generator for local repair
//        private IEnumerable<(string day, int start)> CandidatesLocal(List<dynamic> timetable, TaskUnit t)
//        {
//            string[] DAYS = new[] { "Mon", "Tue", "Wed", "Thu", "Fri" };
//            int HOURS = 7;
//            var daysShuffled = DAYS.OrderBy(_ => Guid.NewGuid()).ToArray();
//            var hours = Enumerable.Range(1, HOURS - t.Duration + 1).OrderBy(_ => Guid.NewGuid()).ToArray();
//            foreach (var d in daysShuffled)
//                foreach (var h in hours)
//                    if (!t.Tried.Contains((d, h)))
//                        yield return (d, h);
//        }

//        [HttpPost("generateCrossDepartmentTimetableBacktracking")]
//        public async Task<IActionResult> GenerateCrossDepartmentTimetableBacktracking([FromBody] TimetableRequest request)
//        {
//            var cs = _configuration.GetConnectionString("DefaultConnection");
//            await using var conn = new NpgsqlConnection(cs);
//            await conn.OpenAsync();
//            await using var tx = await conn.BeginTransactionAsync();

//            try
//            {
//                string[] DAYS = new[] { "Mon", "Tue", "Wed", "Thu", "Fri" };
//                int HOURS = Math.Max(1, request.TotalHoursPerDay);

//                (string staffName, string staffCode) SplitStaff(string staffAssigned)
//                {
//                    if (string.IsNullOrWhiteSpace(staffAssigned)) return ("---", "---");
//                    var name = staffAssigned;
//                    var code = staffAssigned;
//                    if (staffAssigned.Contains("("))
//                    {
//                        var parts = staffAssigned.Split('(', StringSplitOptions.TrimEntries);
//                        name = parts[0].Trim();
//                        code = parts.Length > 1 ? parts[1].Replace(")", "").Trim() : "---";
//                    }
//                    return (name, code);
//                }

//                // Prepare subjects ignoring those without staff assigned
//                var subjects = new List<(string code, string name, string type, int credit, string staff, string labId)>();
//                foreach (var s in request.Subjects ?? Enumerable.Empty<SubjectDto>())
//                {
//                    if (string.IsNullOrWhiteSpace(s.StaffAssigned)) continue;
//                    var type = (s.SubjectType ?? "theory").Trim().ToLower();
//                    subjects.Add((
//                        s.SubjectCode ?? "---",
//                        s.SubjectName ?? "---",
//                        type,
//                        s.Credit,
//                        s.StaffAssigned,
//                        (type == "lab" || type == "embedded") ? (s.LabId?.Trim()) : null
//                    ));
//                }
//                if (subjects.Count == 0)
//                    return BadRequest(new { message = "❌ No valid subjects found (missing staff)." });

//                var staffOcc = new Dictionary<string, Dictionary<string, HashSet<int>>>();
//                var labOcc = new Dictionary<string, Dictionary<string, HashSet<int>>>();

//                void EnsureDayMap<TKey>(Dictionary<TKey, Dictionary<string, HashSet<int>>> map, TKey key)
//                {
//                    if (!map.ContainsKey(key))
//                        map[key] = DAYS.ToDictionary(d => d, d => new HashSet<int>());
//                }

//                // Load staff occupancy
//                using (var cmd = new NpgsqlCommand("SELECT staff_code, day, hour FROM classtimetable", conn, tx))
//                using (var rd = await cmd.ExecuteReaderAsync())
//                {
//                    while (await rd.ReadAsync())
//                    {
//                        var sc = rd["staff_code"]?.ToString() ?? "---";
//                        var day = rd["day"]?.ToString() ?? "Mon";
//                        var hr = Convert.ToInt32(rd["hour"]);
//                        EnsureDayMap(staffOcc, sc);
//                        if (!staffOcc[sc].ContainsKey(day)) staffOcc[sc][day] = new HashSet<int>();
//                        staffOcc[sc][day].Add(hr);
//                    }
//                }
//                // Load lab occupancy
//                using (var cmd = new NpgsqlCommand("SELECT lab_id, day, hour FROM labtimetable", conn, tx))
//                using (var rd = await cmd.ExecuteReaderAsync())
//                {
//                    while (await rd.ReadAsync())
//                    {
//                        var lab = rd["lab_id"]?.ToString() ?? "---";
//                        var day = rd["day"]?.ToString() ?? "Mon";
//                        var hr = Convert.ToInt32(rd["hour"]);
//                        EnsureDayMap(labOcc, lab);
//                        if (!labOcc[lab].ContainsKey(day)) labOcc[lab][day] = new HashSet<int>();
//                        labOcc[lab][day].Add(hr);
//                    }
//                }

//                // Ensure all days map for staff and labs from subjects
//                foreach (var s in subjects)
//                {
//                    var (_, staffCode) = SplitStaff(s.staff);
//                    EnsureDayMap(staffOcc, staffCode);
//                    if (!string.IsNullOrEmpty(s.labId)) EnsureDayMap(labOcc, s.labId);
//                }

//                // Translate subjects into TaskUnits with constraints
//                var tasks = new List<TaskUnit>();
//                var labAllowedBlocks = new List<int[]> { new[] { 1, 2, 3, 4 }, new[] { 4, 5, 6, 7 } };
//                var embeddedLabAllowedBlocks = new List<int[]> {
//                    new[] { 1, 2 }, new[] { 2, 3 }, new[] { 3, 4 }, new[] { 5, 6 }, new[] { 6, 7 }
//                };

//                foreach (var s in subjects)
//                {
//                    switch (s.type)
//                    {
//                        case "lab":
//                            tasks.Add(new TaskUnit
//                            {
//                                SubjectCode = s.code,
//                                SubjectName = s.name,
//                                StaffAssigned = s.staff,
//                                LabId = s.labId,
//                                IsLab = true,
//                                Duration = 4,
//                                Kind = "LAB4"
//                            });
//                            break;
//                        case "embedded":
//                            tasks.Add(new TaskUnit
//                            {
//                                SubjectCode = s.code,
//                                SubjectName = s.name,
//                                StaffAssigned = s.staff,
//                                LabId = s.labId,
//                                IsLab = true,
//                                Duration = 2,
//                                Kind = "EMB_LAB2"
//                            });
//                            tasks.Add(new TaskUnit
//                            {
//                                SubjectCode = s.code,
//                                SubjectName = s.name,
//                                StaffAssigned = s.staff,
//                                LabId = null,
//                                IsLab = false,
//                                Duration = 1,
//                                Kind = "EMB_TH1"
//                            });
//                            tasks.Add(new TaskUnit
//                            {
//                                SubjectCode = s.code,
//                                SubjectName = s.name,
//                                StaffAssigned = s.staff,
//                                LabId = null,
//                                IsLab = false,
//                                Duration = 1,
//                                Kind = "EMB_TH1"
//                            });
//                            break;
//                        default:
//                            int count = Math.Max(0, s.credit);
//                            for (int i = 0; i < count; i++)
//                            {
//                                tasks.Add(new TaskUnit
//                                {
//                                    SubjectCode = s.code,
//                                    SubjectName = s.name,
//                                    StaffAssigned = s.staff,
//                                    LabId = null,
//                                    IsLab = false,
//                                    Duration = 1,
//                                    Kind = "TH1"
//                                });
//                            }
//                            break;
//                    }
//                }

//                // Initialize timetable grid: Dictionary {Day -> {Hour -> slot string}}
//                var timetable = DAYS.Select(d => new
//                {
//                    Day = d,
//                    Slots = Enumerable.Range(1, HOURS).ToDictionary(h => h, _ => "---")
//                }).ToList<dynamic>();

//                bool IsFreeInGrid(string day, int start, int duration)
//                {
//                    var row = timetable.First(t => t.Day == day).Slots;
//                    for (int h = start; h < start + duration; h++)
//                        if (h < 1 || h > HOURS || row[h] != "---") return false;
//                    return true;
//                }

//                void PlaceInGrid(string subjectCode, string staffAssigned, string day, int start, int duration)
//                {
//                    var row = timetable.First(t => t.Day == day).Slots;
//                    for (int h = start; h < start + duration; h++)
//                        row[h] = $"{subjectCode} ({staffAssigned})";
//                }

//                void RemoveFromGrid(string day, int start, int duration)
//                {
//                    var row = timetable.First(t => t.Day == day).Slots;
//                    for (int h = start; h < start + duration; h++)
//                        row[h] = "---";
//                }

//                // For embedded theory tasks: Keep track for non-adjacency
//                var embTheoryPlaced = new Dictionary<string, List<(string day, int hour)>>();

//                bool CanPlaceEmbeddedTheory(TaskUnit t, string day, int hour)
//                {
//                    if (!embTheoryPlaced.ContainsKey(t.SubjectCode)) return true;
//                    foreach (var (d, h) in embTheoryPlaced[t.SubjectCode])
//                    {
//                        if (d == day && (h == hour || h == hour - 1 || h == hour + 1))
//                            return false;
//                    }
//                    return true;
//                }

//                // Modified Fits with DB conflict detection and triggering Repair
//                bool Fits(TaskUnit t, string day, int start)
//                {
//                    if (!IsFreeInGrid(day, start, t.Duration)) return false;

//                    var (_, staffCode) = SplitStaff(t.StaffAssigned);

//                    // Check staff occupancy
//                    for (int h = start; h < start + t.Duration; h++)
//                    {
//                        if (staffOcc.TryGetValue(staffCode, out var dm) && dm.TryGetValue(day, out var set) && set.Contains(h))
//                        {
//                            // Try repairing conflicting DB tasks blocking staff at this slot
//                            if (!_repairAttempted)
//                            {
//                                _repairAttempted = true;
//                                var repaired = RepairConflictingDbTasks(conn, tx, staffOcc, labOcc, timetable, "staff", staffCode, day, h, Fits, Commit, Revert).GetAwaiter().GetResult(); // sync wait inside async method for simplicity
//                                if (!repaired)
//                                {
//                                    _conflictInfo = ("staff", staffCode, day, h);
//                                    throw new RepairFailedException();
//                                }
//                            }
//                            return false; // if repaired, occupancy map is updated, next calls will see free slot
//                        }
//                    }

//                    // Labs occupancy & block constraints
//                    if (t.IsLab)
//                    {
//                        if (!string.IsNullOrEmpty(t.LabId))
//                        {
//                            for (int h = start; h < start + t.Duration; h++)
//                            {
//                                if (labOcc.TryGetValue(t.LabId, out var dm) && dm.TryGetValue(day, out var set) && set.Contains(h))
//                                {
//                                    if (!_repairAttempted)
//                                    {
//                                        _repairAttempted = true;
//                                        var repaired = RepairConflictingDbTasks(conn, tx, staffOcc, labOcc, timetable, "lab", t.LabId, day, h, Fits, Commit, Revert).GetAwaiter().GetResult();
//                                        if (!repaired)
//                                        {
//                                            _conflictInfo = ("lab", t.LabId, day, h);
//                                            throw new RepairFailedException();
//                                        }
//                                    }
//                                    return false;
//                                }
//                            }
//                        }
//                        if (t.Kind == "LAB4")
//                        {
//                            if (!labAllowedBlocks.Any(block => block[0] == start && block.Length == t.Duration))
//                                return false;
//                        }
//                        if (t.Kind == "EMB_LAB2")
//                        {
//                            if (!embeddedLabAllowedBlocks.Any(block => block[0] == start && block.Length == t.Duration))
//                                return false;
//                        }
//                    }

//                    if (t.Kind == "EMB_TH1" && !CanPlaceEmbeddedTheory(t, day, start))
//                        return false;

//                    return true;
//                }

//                // Commit and Revert methods for placing/removing tasks
//                void Commit(TaskUnit t, string day, int start)
//                {
//                    t.Day = day;
//                    t.StartHour = start;
//                    var (_, staffCode) = SplitStaff(t.StaffAssigned);
//                    PlaceInGrid(t.SubjectCode, t.StaffAssigned, day, start, t.Duration);

//                    for (int h = start; h < start + t.Duration; h++)
//                        staffOcc[staffCode][day].Add(h);

//                    if (t.IsLab && !string.IsNullOrEmpty(t.LabId))
//                    {
//                        for (int h = start; h < start + t.Duration; h++)
//                            labOcc[t.LabId][day].Add(h);
//                    }
//                    if (t.Kind == "EMB_TH1")
//                    {
//                        if (!embTheoryPlaced.ContainsKey(t.SubjectCode))
//                            embTheoryPlaced[t.SubjectCode] = new List<(string, int)>();
//                        embTheoryPlaced[t.SubjectCode].Add((day, start));
//                    }
//                }

//                void Revert(TaskUnit t)
//                {
//                    var (_, staffCode) = SplitStaff(t.StaffAssigned);
//                    RemoveFromGrid(t.Day, t.StartHour, t.Duration);

//                    for (int h = t.StartHour; h < t.StartHour + t.Duration; h++)
//                        staffOcc[staffCode][t.Day].Remove(h);

//                    if (t.IsLab && !string.IsNullOrEmpty(t.LabId))
//                    {
//                        for (int h = t.StartHour; h < t.StartHour + t.Duration; h++)
//                            labOcc[t.LabId][t.Day].Remove(h);
//                    }

//                    if (t.Kind == "EMB_TH1" && embTheoryPlaced.ContainsKey(t.SubjectCode))
//                    {
//                        embTheoryPlaced[t.SubjectCode].RemoveAll(x => x.day == t.Day && x.hour == t.StartHour);
//                    }

//                    t.Tried.Add((t.Day, t.StartHour));
//                    t.Day = null;
//                    t.StartHour = 0;
//                }

//                // Candidate slots generators with randomization
//                IEnumerable<(string day, int start)> Candidates(TaskUnit t)
//                {
//                    var daysShuffled = DAYS.OrderBy(_ => Guid.NewGuid()).ToArray();
//                    var hours = Enumerable.Range(1, HOURS - t.Duration + 1).OrderBy(_ => Guid.NewGuid()).ToArray();
//                    foreach (var d in daysShuffled)
//                        foreach (var h in hours)
//                            if (!t.Tried.Contains((d, h)))
//                                yield return (d, h);
//                }

//                // State helpers for flow control of repair attempts & failure info
//                _repairAttempted = false;
//                _conflictInfo = null;

//                // Backtracking solver with integrated repair and failure abort
//                bool Solve(int idx)
//                {
//                    if (idx == tasks.Count) return true;
//                    var t = tasks[idx];

//                    foreach (var (day, start) in Candidates(t))
//                    {
//                        try
//                        {
//                            if (!Fits(t, day, start)) continue;
//                        }
//                        catch (RepairFailedException)
//                        {
//                            return false; // Repair failed - abort generation
//                        }

//                        Commit(t, day, start);
//                        if (Solve(idx + 1)) return true;
//                        Revert(t);
//                    }
//                    return false;
//                }

//                // Allowed lab blocks definitions for constraint checks used globally
//                var labAllowedBlocks1 = new List<int[]> { new[] { 1, 2, 3, 4 }, new[] { 4, 5, 6, 7 } };
//                var embeddedLabAllowedBlocks1 = new List<int[]> {
//                    new[] { 1, 2 }, new[] { 2, 3 }, new[] { 3, 4 }, new[] { 5, 6 }, new[] { 6, 7 }
//                };

//                // Run solver
//                var ok = Solve(0);
//                if (!ok)
//                {
//                    await tx.RollbackAsync();

//                    if (_conflictInfo != null)
//                    {
//                        var (conflictReason, conflictingId, conflictDay, conflictHour) = _conflictInfo.Value;
//                        return Ok(new
//                        {
//                            message = "❌ Timetable generation failed due to unsolvable conflicts after repair attempts.",
//                            conflictReason = conflictReason,
//                            conflictingId = conflictingId,
//                            day = conflictDay,
//                            hour = conflictHour,
//                            suggestion = "Please reassign staff or adjust hours manually."
//                        });
//                    }
//                    else
//                    {
//                        return Ok(new
//                        {
//                            message = "❌ Timetable generation failed - unable to place all tasks without conflicts."
//                        });
//                    }
//                }

//                // Commit all tasks to DB inside one transaction, backing up all original rows first
//                foreach (var t in tasks)
//                {
//                    var (staffName, staffCode) = SplitStaff(t.StaffAssigned);

//                    for (int h = t.StartHour; h < t.StartHour + t.Duration; h++)
//                    {
//                        using (var icClass = new NpgsqlCommand(@"
//                        INSERT INTO classtimetable
//                        (staff_name, staff_code, department_id, year, semester, section, day, hour, subject_code, subject_name)
//                        VALUES
//                        (@staff_name, @staff_code, @department, @year, @semester, @section, @day, @hour, @subject_code, @subject_name);", conn, tx))
//                        {
//                            icClass.Parameters.AddWithValue("@staff_name", staffName ?? "---");
//                            icClass.Parameters.AddWithValue("@staff_code", staffCode ?? "---");
//                            icClass.Parameters.AddWithValue("@department", request.Department ?? "---");
//                            icClass.Parameters.AddWithValue("@year", request.Year ?? "---");
//                            icClass.Parameters.AddWithValue("@semester", request.Semester ?? "---");
//                            icClass.Parameters.AddWithValue("@section", request.Section ?? "---");
//                            icClass.Parameters.AddWithValue("@day", t.Day ?? "---");
//                            icClass.Parameters.AddWithValue("@hour", h);
//                            icClass.Parameters.AddWithValue("@subject_code", t.SubjectCode ?? "---");
//                            icClass.Parameters.AddWithValue("@subject_name", t.SubjectName ?? "---");
//                            await icClass.ExecuteNonQueryAsync();
//                        }
//                    }

//                    if (t.IsLab && !string.IsNullOrEmpty(t.LabId))
//                    {
//                        // Back up original lab rows before insertion
//                        using var backupLab = new NpgsqlCommand(@"
//                            INSERT INTO labtimetable_backup SELECT * FROM labtimetable
//                            WHERE lab_id = @lab AND day = @day AND hour BETWEEN @start AND @end;", conn, tx);
//                        backupLab.Parameters.AddWithValue("@lab", t.LabId);
//                        backupLab.Parameters.AddWithValue("@day", t.Day);
//                        backupLab.Parameters.AddWithValue("@start", t.StartHour);
//                        backupLab.Parameters.AddWithValue("@end", t.StartHour + t.Duration - 1);
//                        await backupLab.ExecuteNonQueryAsync();

//                        // Delete original lab hours if any
//                        using var deleteLab = new NpgsqlCommand(@"
//                            DELETE FROM labtimetable
//                            WHERE lab_id = @lab AND day = @day AND hour BETWEEN @start AND @end;", conn, tx);
//                        deleteLab.Parameters.AddWithValue("@lab", t.LabId);
//                        deleteLab.Parameters.AddWithValue("@day", t.Day);
//                        deleteLab.Parameters.AddWithValue("@start", t.StartHour);
//                        deleteLab.Parameters.AddWithValue("@end", t.StartHour + t.Duration - 1);
//                        await deleteLab.ExecuteNonQueryAsync();

//                        // Insert new lab hours
//                        for (int h = t.StartHour; h < t.StartHour + t.Duration; h++)
//                        {
//                            // If lab starts at hour 4, only insert for hours 5, 6, 7
//                            if (t.StartHour == 4 && h == 4)
//                                continue; // Skip hour 4 in lab timetable

//                            using var icLab = new NpgsqlCommand(@"
//        INSERT INTO labtimetable
//        (lab_id, subject_code, subject_name, staff_name, department, year, semester, section, day, hour)
//        VALUES
//        (@lab_id, @subject_code, @subject_name, @staff_name, @department, @year, @semester, @section, @day, @hour);", conn, tx);

//                            icLab.Parameters.AddWithValue("@lab_id", t.LabId ?? (object)DBNull.Value);
//                            icLab.Parameters.AddWithValue("@subject_code", t.SubjectCode ?? "---");
//                            icLab.Parameters.AddWithValue("@subject_name", t.SubjectName ?? "---");
//                            icLab.Parameters.AddWithValue("@staff_name", staffName ?? "---");
//                            icLab.Parameters.AddWithValue("@department", request.Department ?? "---");
//                            icLab.Parameters.AddWithValue("@year", request.Year ?? "---");
//                            icLab.Parameters.AddWithValue("@semester", request.Semester ?? "---");
//                            icLab.Parameters.AddWithValue("@section", request.Section ?? "---");
//                            icLab.Parameters.AddWithValue("@day", t.Day ?? "---");
//                            icLab.Parameters.AddWithValue("@hour", h);
//                            await icLab.ExecuteNonQueryAsync();
//                        }

//                    }
//                }

//                await tx.CommitAsync();

//                var responseTimetable = timetable.Select(row => new
//                {
//                    Day = row.Day,
//                    HourlySlots = row.Slots
//                }).ToList();

//                return Ok(new
//                {
//                    message = "✅ Timetable generated with local repair and transaction rollback on failure, respecting all constraints.",
//                    timetable = responseTimetable,
//                    usedLabIds = tasks.Where(x => x.IsLab && !string.IsNullOrEmpty(x.LabId)).Select(x => x.LabId).Distinct().ToList(),
//                    receivedPayload = request
//                });
//            }
//            catch (RepairFailedException)
//            {
//                await tx.RollbackAsync();

//                if (_conflictInfo != null)
//                {
//                    var (conflictReason, conflictingId, conflictDay, conflictHour) = _conflictInfo.Value;
//                    return Ok(new
//                    {
//                        message = "❌ Timetable generation failed due to unrecoverable conflicts after repair attempts.",
//                        conflictReason = conflictReason,
//                        conflictingId = conflictingId,
//                        day = conflictDay,
//                        hour = conflictHour,
//                        suggestion = "Please reassign staff or adjust hours manually."
//                    });
//                }

//                return Ok(new { message = "❌ Timetable generation failed after repair attempts." });
//            }
//            catch (Exception ex)
//            {
//                await tx.RollbackAsync();
//                return StatusCode(500, new { message = "❌ Internal Server Error while generating timetable.", error = ex.Message });
//            }
//        }

//        // Exception to signal that repair failed and generation must stop
//        private class RepairFailedException : Exception { }

//        // Tracker flags for repair attempt and failure info
//        private bool _repairAttempted = false;
//        private (string reason, string conflictingId, string day, int hour)? _conflictInfo = null;

//        // Allowed continuous blocks helpers for constraints used inside Fits
//        private static readonly List<int[]> labAllowedBlocks = new List<int[]> { new[] { 1, 2, 3, 4 }, new[] { 4, 5, 6, 7 } };
//        private static readonly List<int[]> embeddedLabAllowedBlocks = new List<int[]> {
//            new[] { 1, 2 }, new[] { 2, 3 }, new[] { 3, 4 }, new[] { 5, 6 }, new[] { 6, 7 }
//        };
//    }
//}
