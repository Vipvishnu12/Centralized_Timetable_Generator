
//tabu search
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
//            public bool IsPlaced = false;
//        }

//        [HttpPost("generateCrossDepartmentTimetableBacktracking")]
//        public async Task<IActionResult> GenerateCrossDepartmentTimetableTabuSearch([FromBody] TimetableRequest request)
//        {
//            var cs = _configuration.GetConnectionString("DefaultConnection");

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
//                using var conn = new NpgsqlConnection(cs);
//                await conn.OpenAsync();

//                void EnsureDayMap<TKey>(Dictionary<TKey, Dictionary<string, HashSet<int>>> map, TKey key)
//                {
//                    if (!map.ContainsKey(key))
//                        map[key] = DAYS.ToDictionary(d => d, d => new HashSet<int>());
//                }

//                // Load existing occupancy
//                using (var cmd = new NpgsqlCommand("SELECT staff_code, day, hour FROM classtimetable", conn))
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
//                using (var cmd = new NpgsqlCommand("SELECT lab_id, day, hour FROM labtimetable", conn))
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
//                foreach (var s in subjects)
//                {
//                    var (_, staffCode) = SplitStaff(s.staff);
//                    EnsureDayMap(staffOcc, staffCode);
//                    if (!string.IsNullOrEmpty(s.labId)) EnsureDayMap(labOcc, s.labId);
//                }

//                // Translate subjects to tasks similarly as before
//                var tasks = new List<TaskUnit>();
//                var labAllowedBlocks = new List<int[]> { new[] { 1, 2, 3, 4 }, new[] { 4, 5, 6, 7 } };
//                var embeddedLabAllowedBlocks = new List<int[]> {
//                    new[] { 1, 2 },
//                    new[] { 2, 3 },
//                    new[] { 3, 4 },
//                    new[] { 5, 6 },
//                    new[] { 6, 7 }
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
//                                IsLab = false,
//                                Duration = 1,
//                                Kind = "EMB_TH1"
//                            });
//                            tasks.Add(new TaskUnit
//                            {
//                                SubjectCode = s.code,
//                                SubjectName = s.name,
//                                StaffAssigned = s.staff,
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
//                                    IsLab = false,
//                                    Duration = 1,
//                                    Kind = "TH1"
//                                });
//                            }
//                            break;
//                    }
//                }

//                // Initialize timetable grid from DB snapshot to preserve existing schedule
//                var timetable = DAYS.ToDictionary(d => d,
//                    d => Enumerable.Range(1, HOURS).ToDictionary(h => h, _ => "---"));

//                // Helper functions similar to before
//                bool IsFreeInGrid(string day, int start, int duration)
//                {
//                    var row = timetable[day];
//                    for (int h = start; h < start + duration; h++)
//                        if (h < 1 || h > HOURS || row[h] != "---") return false;
//                    return true;
//                }

//                bool CanPlace(TaskUnit t, string day, int start)
//                {
//                    if (!IsFreeInGrid(day, start, t.Duration)) return false;

//                    var (_, staffCode) = SplitStaff(t.StaffAssigned);

//                    for (int h = start; h < start + t.Duration; h++)
//                    {
//                        if (staffOcc.TryGetValue(staffCode, out var dm) && dm.TryGetValue(day, out var set) && set.Contains(h))
//                            return false;
//                        if (t.IsLab && !string.IsNullOrEmpty(t.LabId)
//                            && labOcc.TryGetValue(t.LabId, out var ldm) && ldm.TryGetValue(day, out var lset) && lset.Contains(h))
//                            return false;
//                    }

//                    if (t.IsLab)
//                    {
//                        if (t.Kind == "LAB4")
//                        {
//                            if (!labAllowedBlocks.Any(block => block[0] == start && block.Length == t.Duration))
//                                return false;
//                        }
//                        else if (t.Kind == "EMB_LAB2")
//                        {
//                            if (!embeddedLabAllowedBlocks.Any(block => block[0] == start && block.Length == t.Duration))
//                                return false;
//                        }
//                    }

//                    return true;
//                }

//                // Place and Remove functions update timetable and occupancy
//                void Place(TaskUnit t, string day, int start)
//                {
//                    var row = timetable[day];
//                    var (_, staffCode) = SplitStaff(t.StaffAssigned);

//                    for (int h = start; h < start + t.Duration; h++)
//                    {
//                        row[h] = $"{t.SubjectCode} ({t.StaffAssigned})";
//                        staffOcc[staffCode][day].Add(h);
//                        if (t.IsLab && !string.IsNullOrEmpty(t.LabId))
//                            labOcc[t.LabId][day].Add(h);
//                    }

//                    t.Day = day;
//                    t.StartHour = start;
//                    t.IsPlaced = true;
//                }

//                void Remove(TaskUnit t)
//                {
//                    if (!t.IsPlaced) return;
//                    var row = timetable[t.Day];
//                    var (_, staffCode) = SplitStaff(t.StaffAssigned);

//                    for (int h = t.StartHour; h < t.StartHour + t.Duration; h++)
//                    {
//                        row[h] = "---";
//                        staffOcc[staffCode][t.Day].Remove(h);
//                        if (t.IsLab && !string.IsNullOrEmpty(t.LabId))
//                            labOcc[t.LabId][t.Day].Remove(h);
//                    }

//                    t.IsPlaced = false;
//                    t.Day = null;
//                    t.StartHour = 0;
//                }

//                Random rnd = new Random();

//                // Tabu List to temporarily forbid recent moves to avoid cycling
//                var tabuList = new Queue<(TaskUnit task, string day, int start)>();
//                int tabuTenure = 7; // Number of iterations a move remains tabu

//                // For iterative improvement: assign random initial placements where possible
//                foreach (var task in tasks)
//                {
//                    var possibleSlots = new List<(string day, int start)>();
//                    foreach (var day in DAYS)
//                    {
//                        for (int hour = 1; hour <= HOURS - task.Duration + 1; hour++)
//                        {
//                            if (CanPlace(task, day, hour))
//                                possibleSlots.Add((day, hour));
//                        }
//                    }
//                    if (possibleSlots.Count == 0)
//                    {
//                        // No feasible slot for this task => fail generation, inform user
//                        return Ok(new
//                        {
//                            message = $"⚠ Cannot place task {task.SubjectCode} {task.SubjectName} without conflicts. Please adjust inputs.",
//                            receivedPayload = request
//                        });
//                    }
//                    // Place randomly initially
//                    var choice = possibleSlots[rnd.Next(possibleSlots.Count)];
//                    Place(task, choice.day, choice.start);
//                }

//                // Now attempt to improve by trying to fix conflicts and remove tabu moves
//                int maxIterations = 1000;
//                bool improvement;
//                for (int iter = 0; iter < maxIterations; iter++)
//                {
//                    improvement = false;

//                    foreach (var task in tasks)
//                    {
//                        // Try to find a better slot if any conflicts appear (rare here since placed validly)
//                        // Here we simulate local search by trying to move task to another feasible slot with tabu check

//                        var candidates = new List<(string day, int start)>();
//                        foreach (var day in DAYS)
//                        {
//                            for (int hour = 1; hour <= HOURS - task.Duration + 1; hour++)
//                            {
//                                if (CanPlace(task, day, hour) && !tabuList.Contains((task, day, hour)))
//                                    candidates.Add((day, hour));
//                            }
//                        }

//                        if (candidates.Count > 0)
//                        {
//                            var currentPos = (task.Day, task.StartHour);
//                            var newPos = candidates[rnd.Next(candidates.Count)];

//                            // Move to new position
//                            Remove(task);
//                            Place(task, newPos.day, newPos.start);
//                            tabuList.Enqueue((task, currentPos.Item1, currentPos.Item2));
//                            if (tabuList.Count > tabuTenure) tabuList.Dequeue();

//                            improvement = true;
//                        }
//                    }

//                    if (!improvement)
//                        break;
//                }

//                // After iterations, check feasibility of entire timetable (no conflicts)
//                foreach (var task in tasks)
//                {
//                    if (!task.IsPlaced)
//                    {
//                        return Ok(new
//                        {
//                            message = "⚠ Could not resolve scheduling conflicts after local search.",
//                            receivedPayload = request
//                        });
//                    }
//                }

//                // Write timetable data to DB inside a transaction; if any error, rollback without changes
//                using (var tran = conn.BeginTransaction())
//                {
//                    // Delete previous timetable data for this department+year+semester+section to avoid duplication
//                    using (var delClass = new NpgsqlCommand(@"
//                        DELETE FROM classtimetable 
//                        WHERE department_id=@department AND year=@year AND semester=@semester AND section=@section;", conn, tran))
//                    {
//                        delClass.Parameters.AddWithValue("@department", request.Department ?? "---");
//                        delClass.Parameters.AddWithValue("@year", request.Year ?? "---");
//                        delClass.Parameters.AddWithValue("@semester", request.Semester ?? "---");
//                        delClass.Parameters.AddWithValue("@section", request.Section ?? "---");
//                        await delClass.ExecuteNonQueryAsync();
//                    }
//                    using (var delLab = new NpgsqlCommand(@"
//                        DELETE FROM labtimetable
//                        WHERE department=@department AND year=@year AND semester=@semester AND section=@section;", conn, tran))
//                    {
//                        delLab.Parameters.AddWithValue("@department", request.Department ?? "---");
//                        delLab.Parameters.AddWithValue("@year", request.Year ?? "---");
//                        delLab.Parameters.AddWithValue("@semester", request.Semester ?? "---");
//                        delLab.Parameters.AddWithValue("@section", request.Section ?? "---");
//                        await delLab.ExecuteNonQueryAsync();
//                    }

//                    foreach (var t in tasks)
//                    {
//                        var (staffName, staffCode) = SplitStaff(t.StaffAssigned);

//                        for (int h = t.StartHour; h < t.StartHour + t.Duration; h++)
//                        {
//                            using (var icClass = new NpgsqlCommand(@"
//                                INSERT INTO classtimetable
//                                (staff_name, staff_code, department_id, year, semester, section, day, hour, subject_code, subject_name)
//                                VALUES
//                                (@staff_name, @staff_code, @department, @year, @semester, @section, @day, @hour, @subject_code, @subject_name);", conn, tran))
//                            {
//                                icClass.Parameters.AddWithValue("@staff_name", staffName ?? "---");
//                                icClass.Parameters.AddWithValue("@staff_code", staffCode ?? "---");
//                                icClass.Parameters.AddWithValue("@department", request.Department ?? "---");
//                                icClass.Parameters.AddWithValue("@year", request.Year ?? "---");
//                                icClass.Parameters.AddWithValue("@semester", request.Semester ?? "---");
//                                icClass.Parameters.AddWithValue("@section", request.Section ?? "---");
//                                icClass.Parameters.AddWithValue("@day", t.Day ?? "---");
//                                icClass.Parameters.AddWithValue("@hour", h);
//                                icClass.Parameters.AddWithValue("@subject_code", t.SubjectCode ?? "---");
//                                icClass.Parameters.AddWithValue("@subject_name", t.SubjectName ?? "---");
//                                await icClass.ExecuteNonQueryAsync();
//                            }
//                        }
//                        if (t.IsLab && !string.IsNullOrEmpty(t.LabId))
//                        {
//                            int labStart = t.StartHour;
//                            if (labStart == 4)
//                            {
//                                labStart = 5; // Adjust as per your logic
//                            }
//                            for (int h = labStart; h < t.StartHour + t.Duration; h++)
//                            {
//                                using (var icLab = new NpgsqlCommand(@"
//                                    INSERT INTO labtimetable
//                                    (lab_id, subject_code, subject_name, staff_name, department, year, semester, section, day, hour)
//                                    VALUES
//                                    (@lab_id, @subject_code, @subject_name, @staff_name, @department, @year, @semester, @section, @day, @hour);", conn, tran))
//                                {
//                                    icLab.Parameters.AddWithValue("@lab_id", t.LabId ?? (object)DBNull.Value);
//                                    icLab.Parameters.AddWithValue("@subject_code", t.SubjectCode ?? "---");
//                                    icLab.Parameters.AddWithValue("@subject_name", t.SubjectName ?? "---");
//                                    icLab.Parameters.AddWithValue("@staff_name", staffName ?? "---");
//                                    icLab.Parameters.AddWithValue("@department", request.Department ?? "---");
//                                    icLab.Parameters.AddWithValue("@year", request.Year ?? "---");
//                                    icLab.Parameters.AddWithValue("@semester", request.Semester ?? "---");
//                                    icLab.Parameters.AddWithValue("@section", request.Section ?? "---");
//                                    icLab.Parameters.AddWithValue("@day", t.Day ?? "---");
//                                    icLab.Parameters.AddWithValue("@hour", h);
//                                    await icLab.ExecuteNonQueryAsync();
//                                }
//                            }
//                        }
//                    }
//                    await tran.CommitAsync();
//                }

//                // Prepare response schedule view
//                var responseTimetable = timetable.Select(row => new
//                {
//                    Day = row.Key,
//                    HourlySlots = row.Value
//                }).ToList();

//                return Ok(new
//                {
//                    message = "✅ Timetable generated successfully with Tabu Search local search optimization.",
//                    timetable = responseTimetable,
//                    usedLabIds = tasks.Where(x => x.IsLab && !string.IsNullOrEmpty(x.LabId)).Select(x => x.LabId).Distinct().ToList(),
//                    receivedPayload = request
//                });
//            }
//            catch (Exception ex)
//            {
//                return StatusCode(500, new { message = "❌ Internal Server Error while generating timetable.", error = ex.Message });
//            }
//        }
//    }
//}











//perfect without shuffle
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
//        public class TaskUnit
//        {
//            public string SubjectCode;
//            public string SubjectName;
//            public string StaffAssigned;
//            public string LabId;
//            public bool IsLab;
//            public int Duration;
//            public string Kind;
//            public string Day;      // Assigned day
//            public int StartHour;   // Assigned start hour
//            public bool IsPlaced = false;

//            // Domain holds all possible (day, start) positions this task can be assigned for constraint propagation
//            public List<(string day, int start)> Domain = new();
//        }

//        [HttpPost("generateCrossDepartmentTimetableBacktracking")]
//        public async Task<IActionResult> GenerateCrossDepartmentTimetableBacktracking([FromBody] TimetableRequest request)
//        {
//            var cs = _configuration.GetConnectionString("DefaultConnection");
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

//                using var conn = new NpgsqlConnection(cs);
//                await conn.OpenAsync();

//                // Load existing occupancy from DB - staff and lab occupancy to consider pre-existing timetable blocks
//                var staffOcc = new Dictionary<string, Dictionary<string, HashSet<int>>>();
//                var labOcc = new Dictionary<string, Dictionary<string, HashSet<int>>>();
//                void EnsureDayMap<TKey>(Dictionary<TKey, Dictionary<string, HashSet<int>>> map, TKey key)
//                {
//                    if (!map.ContainsKey(key))
//                        map[key] = DAYS.ToDictionary(d => d, d => new HashSet<int>());
//                }

//                // Load existing staff occupancy
//                using (var cmd = new NpgsqlCommand("SELECT staff_code, day, hour FROM classtimetable", conn))
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

//                // Load existing lab occupancy
//                using (var cmd = new NpgsqlCommand("SELECT lab_id, day, hour FROM labtimetable", conn))
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

//                // Ensure all relevant keys in occupancy dictionaries
//                foreach (var s in subjects)
//                {
//                    var (_, staffCode) = SplitStaff(s.staff);
//                    EnsureDayMap(staffOcc, staffCode);
//                    if (!string.IsNullOrEmpty(s.labId)) EnsureDayMap(labOcc, s.labId);
//                }

//                // Translate subjects to TaskUnits to be scheduled
//                var tasks = new List<TaskUnit>();
//                var labAllowedBlocks = new List<int[]> { new[] { 1, 2, 3, 4 }, new[] { 4, 5, 6, 7 } };
//                var embeddedLabAllowedBlocks = new List<int[]> {
//                    new[] { 1, 2 },
//                    new[] { 2, 3 },
//                    new[] { 3, 4 },
//                    new[] { 5, 6 },
//                    new[] { 6, 7 }
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
//                                IsLab = false,
//                                Duration = 1,
//                                Kind = "EMB_TH1"
//                            });
//                            tasks.Add(new TaskUnit
//                            {
//                                SubjectCode = s.code,
//                                SubjectName = s.name,
//                                StaffAssigned = s.staff,
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
//                                    IsLab = false,
//                                    Duration = 1,
//                                    Kind = "TH1"
//                                });
//                            }
//                            break;
//                    }
//                }

//                // Initialize timetable grid as dictionary for return formatting
//                var timetable = DAYS.ToDictionary(d => d,
//                    d => Enumerable.Range(1, HOURS).ToDictionary(h => h, _ => "---"));

//                // Check if start hour fits duration and allowed lab blocks for lab tasks
//                bool IsValidStartForLab(TaskUnit t, int start)
//                {
//                    if (t.Kind == "LAB4")
//                        return labAllowedBlocks.Any(b => b[0] == start && b.Length == t.Duration);
//                    if (t.Kind == "EMB_LAB2")
//                        return embeddedLabAllowedBlocks.Any(b => b[0] == start && b.Length == t.Duration);
//                    return true;
//                }

//                // Return true if time slots are free in timetable grid and no conflicts exist for staff and lab
//                bool IsFreeAndNoConflict(TaskUnit t, string day, int start,
//                    Dictionary<string, Dictionary<string, HashSet<int>>> staffOccMap,
//                    Dictionary<string, Dictionary<string, HashSet<int>>> labOccMap)
//                {
//                    // Check timetable grid free
//                    for (int h = start; h < start + t.Duration; h++)
//                    {
//                        if (h < 1 || h > HOURS) return false;
//                        if (timetable[day][h] != "---") return false;
//                    }
//                    var (_, staffCode) = SplitStaff(t.StaffAssigned);

//                    // Check staff is free at all hours
//                    for (int h = start; h < start + t.Duration; h++)
//                    {
//                        if (staffOccMap.TryGetValue(staffCode, out var dayMap) && dayMap.TryGetValue(day, out var hoursSet) && hoursSet.Contains(h))
//                            return false;
//                    }

//                    // If lab, also check lab availability
//                    if (t.IsLab && !string.IsNullOrEmpty(t.LabId))
//                    {
//                        for (int h = start; h < start + t.Duration; h++)
//                        {
//                            if (labOccMap.TryGetValue(t.LabId, out var labDayMap) && labDayMap.TryGetValue(day, out var hoursSet) && hoursSet.Contains(h))
//                                return false;
//                        }
//                        // Check allowed lab start blocks
//                        if (!IsValidStartForLab(t, start))
//                            return false;
//                    }

//                    return true;
//                }

//                // Constraint Propagation using AC-3-style filtering of domains
//                // We maintain domain of each task as allowed (day, start) pairs
//                // At each assignment, domains of other tasks are reduced to discard conflicting slots
//                // This early pruning helps avoid hopeless paths.

//                // Build initial domains for each task (all feasible slots)
//                foreach (var t in tasks)
//                {
//                    t.Domain.Clear();
//                    foreach (var day in DAYS)
//                    {
//                        for (int h = 1; h <= HOURS - t.Duration + 1; h++)
//                        {
//                            if (IsFreeAndNoConflict(t, day, h, staffOcc, labOcc))
//                            {
//                                t.Domain.Add((day, h));
//                            }
//                        }
//                    }

//                    if (t.Domain.Count == 0)
//                    {
//                        // If any task has empty domain initially, fail now.
//                        return Ok(new
//                        {
//                            message = $"⚠ No feasible slot initially for task {t.SubjectCode} {t.SubjectName}. Cannot generate timetable.",
//                            receivedPayload = request
//                        });
//                    }
//                }

//                // Updates timetable & occupancy on assignment
//                void AssignTask(TaskUnit t, (string day, int start) slot)
//                {
//                    var (day, start) = slot;
//                    var (_, staffCode) = SplitStaff(t.StaffAssigned);

//                    for (int h = start; h < start + t.Duration; h++)
//                    {
//                        timetable[day][h] = $"{t.SubjectCode} ({t.StaffAssigned})";
//                        if (!staffOcc.ContainsKey(staffCode)) staffOcc[staffCode] = new Dictionary<string, HashSet<int>>();
//                        if (!staffOcc[staffCode].ContainsKey(day)) staffOcc[staffCode][day] = new HashSet<int>();
//                        staffOcc[staffCode][day].Add(h);

//                        if (t.IsLab && !string.IsNullOrEmpty(t.LabId))
//                        {
//                            if (!labOcc.ContainsKey(t.LabId)) labOcc[t.LabId] = new Dictionary<string, HashSet<int>>();
//                            if (!labOcc[t.LabId].ContainsKey(day)) labOcc[t.LabId][day] = new HashSet<int>();
//                            labOcc[t.LabId][day].Add(h);
//                        }
//                    }
//                    t.Day = day;
//                    t.StartHour = start;
//                    t.IsPlaced = true;
//                }

//                // Removes timetable & occupancy on unassignment
//                void UnassignTask(TaskUnit t)
//                {
//                    if (!t.IsPlaced) return;
//                    var day = t.Day;
//                    var start = t.StartHour;
//                    var (_, staffCode) = SplitStaff(t.StaffAssigned);

//                    for (int h = start; h < start + t.Duration; h++)
//                    {
//                        timetable[day][h] = "---";
//                        staffOcc[staffCode][day].Remove(h);

//                        if (t.IsLab && !string.IsNullOrEmpty(t.LabId))
//                        {
//                            labOcc[t.LabId][day].Remove(h);
//                        }
//                    }
//                    t.IsPlaced = false;
//                    t.Day = null;
//                    t.StartHour = 0;
//                }

//                // Forward checking / domain filtering after assigning task t with slot assignedSlot
//                // This implements constraint propagation to remove impossible options for other tasks
//                bool PropagateConstraints(List<TaskUnit> tasksList, TaskUnit assignedTask, (string day, int start) assignedSlot)
//                {
//                    // We create a copy of domains to roll back if needed
//                    var snapshotDomains = tasksList.ToDictionary(t => t, t => t.Domain.ToList());

//                    // Assign the domain of the assigned task to a single choice
//                    assignedTask.Domain = new List<(string, int)> { assignedSlot };

//                    // For each unassigned task, remove all domain values conflicting with this assignment
//                    foreach (var other in tasksList)
//                    {
//                        if (other == assignedTask || other.IsPlaced)
//                            continue;

//                        var filteredDomain = new List<(string day, int start)>();
//                        foreach (var pos in other.Domain)
//                        {
//                            // Check if pos conflicts with assignedSlot on resources: staff, lab, timetable slots, max consecutive hours could also be checked here
//                            bool conflict = false;
//                            // Overlap check: if same day and overlapping hour intervals
//                            if (pos.day == assignedSlot.day)
//                            {
//                                int start1 = pos.start;
//                                int end1 = pos.start + other.Duration - 1;
//                                int start2 = assignedSlot.start;
//                                int end2 = assignedSlot.start + assignedTask.Duration - 1;
//                                bool hoursOverlap = end1 >= start2 && end2 >= start1;

//                                if (hoursOverlap)
//                                {
//                                    // Check if same staff assigned conflicts
//                                    var (_, staffCode1) = SplitStaff(other.StaffAssigned);
//                                    var (_, staffCode2) = SplitStaff(assignedTask.StaffAssigned);
//                                    if (staffCode1 == staffCode2) conflict = true;

//                                    // Check if labs conflict (if lab tasks)
//                                    if (!conflict && other.IsLab && assignedTask.IsLab && !string.IsNullOrEmpty(other.LabId) && !string.IsNullOrEmpty(assignedTask.LabId))
//                                    {
//                                        if (other.LabId == assignedTask.LabId) conflict = true;
//                                    }

//                                    // Additional constraints can be added here, e.g., max consecutive hours per staff or special room usage rules.
//                                }
//                            }

//                            if (!conflict)
//                            {
//                                filteredDomain.Add(pos);
//                            }
//                        }

//                        other.Domain = filteredDomain;

//                        // If domain wiped out, propagation fails
//                        if (other.Domain.Count == 0)
//                        {
//                            // rollback domains before returning false
//                            foreach (var kvp in snapshotDomains)
//                            {
//                                kvp.Key.Domain = kvp.Value;
//                            }
//                            return false;
//                        }
//                    }
//                    return true;
//                }

//                // Backtracking search with constraint propagation
//                // Tries to assign each unassigned task one by one, applying constraint propagation after each assignment
//                bool Backtrack(List<TaskUnit> tasksList)
//                {
//                    // All tasks assigned? return true success
//                    if (tasksList.All(t => t.IsPlaced)) return true;

//                    // Select next unassigned task using MRV (Minimum Remaining Values) heuristic for better pruning
//                    var unassignedTasks = tasksList.Where(t => !t.IsPlaced).ToList();
//                    var nextTask = unassignedTasks.OrderBy(t => t.Domain.Count).First();

//                    // Try each domain value for next task
//                    foreach (var slot in nextTask.Domain.OrderBy(s => s.day).ThenBy(s => s.start))
//                    {
//                        if (IsFreeAndNoConflict(nextTask, slot.day, slot.start, staffOcc, labOcc))
//                        {
//                            AssignTask(nextTask, slot);

//                            // Propagate constraints after this assignment, prune infeasible future assignments
//                            bool consistent = PropagateConstraints(tasksList, nextTask, slot);

//                            if (consistent)
//                            {
//                                if (Backtrack(tasksList))
//                                    return true; // success propagated
//                            }

//                            // Backtrack: unassign current task and restore states
//                            UnassignTask(nextTask);
//                        }
//                    }
//                    // No valid assignment for this task found => backtrack further
//                    return false;
//                }

//                // Begin backtracking with Constraint Propagation
//                bool solved = Backtrack(tasks);

//                if (!solved)
//                {
//                    return Ok(new
//                    {
//                        message = "⚠ No solution found with constraint propagation and backtracking, timetable generation failed.",
//                        receivedPayload = request
//                    });
//                }

//                // Store final solution in DB inside a transaction; rollback if any error

//                using (var tran = conn.BeginTransaction())
//                {
//                    // Delete previous timetable data for this dept/year/sem/section to avoid duplication
//                    using (var delClass = new NpgsqlCommand(@"
//                        DELETE FROM classtimetable 
//                        WHERE department_id=@department AND year=@year AND semester=@semester AND section=@section;", conn, tran))
//                    {
//                        delClass.Parameters.AddWithValue("@department", request.Department ?? "---");
//                        delClass.Parameters.AddWithValue("@year", request.Year ?? "---");
//                        delClass.Parameters.AddWithValue("@semester", request.Semester ?? "---");
//                        delClass.Parameters.AddWithValue("@section", request.Section ?? "---");
//                        await delClass.ExecuteNonQueryAsync();
//                    }
//                    using (var delLab = new NpgsqlCommand(@"
//                        DELETE FROM labtimetable
//                        WHERE department=@department AND year=@year AND semester=@semester AND section=@section;", conn, tran))
//                    {
//                        delLab.Parameters.AddWithValue("@department", request.Department ?? "---");
//                        delLab.Parameters.AddWithValue("@year", request.Year ?? "---");
//                        delLab.Parameters.AddWithValue("@semester", request.Semester ?? "---");
//                        delLab.Parameters.AddWithValue("@section", request.Section ?? "---");
//                        await delLab.ExecuteNonQueryAsync();
//                    }

//                    // Insert new assignments into DB
//                    foreach (var t in tasks)
//                    {
//                        var (staffName, staffCode) = SplitStaff(t.StaffAssigned);
//                        for (int h = t.StartHour; h < t.StartHour + t.Duration; h++)
//                        {
//                            using (var icClass = new NpgsqlCommand(@"
//                                INSERT INTO classtimetable
//                                (staff_name, staff_code, department_id, year, semester, section, day, hour, subject_code, subject_name)
//                                VALUES
//                                (@staff_name, @staff_code, @department, @year, @semester, @section, @day, @hour, @subject_code, @subject_name);", conn, tran))
//                            {
//                                icClass.Parameters.AddWithValue("@staff_name", staffName ?? "---");
//                                icClass.Parameters.AddWithValue("@staff_code", staffCode ?? "---");
//                                icClass.Parameters.AddWithValue("@department", request.Department ?? "---");
//                                icClass.Parameters.AddWithValue("@year", request.Year ?? "---");
//                                icClass.Parameters.AddWithValue("@semester", request.Semester ?? "---");
//                                icClass.Parameters.AddWithValue("@section", request.Section ?? "---");
//                                icClass.Parameters.AddWithValue("@day", t.Day ?? "---");
//                                icClass.Parameters.AddWithValue("@hour", h);
//                                icClass.Parameters.AddWithValue("@subject_code", t.SubjectCode ?? "---");
//                                icClass.Parameters.AddWithValue("@subject_name", t.SubjectName ?? "---");
//                                await icClass.ExecuteNonQueryAsync();
//                            }
//                        }
//                        if (t.IsLab && !string.IsNullOrEmpty(t.LabId))
//                        {
//                            int labStart = t.StartHour;
//                            if (labStart == 4)
//                            {
//                                labStart = 5; // Previous logic - adjust as needed
//                            }
//                            for (int h = labStart; h < t.StartHour + t.Duration; h++)
//                            {
//                                using (var icLab = new NpgsqlCommand(@"
//                                    INSERT INTO labtimetable
//                                    (lab_id, subject_code, subject_name, staff_name, department, year, semester, section, day, hour)
//                                    VALUES
//                                    (@lab_id, @subject_code, @subject_name, @staff_name, @department, @year, @semester, @section, @day, @hour);", conn, tran))
//                                {
//                                    icLab.Parameters.AddWithValue("@lab_id", t.LabId ?? (object)DBNull.Value);
//                                    icLab.Parameters.AddWithValue("@subject_code", t.SubjectCode ?? "---");
//                                    icLab.Parameters.AddWithValue("@subject_name", t.SubjectName ?? "---");
//                                    icLab.Parameters.AddWithValue("@staff_name", staffName ?? "---");
//                                    icLab.Parameters.AddWithValue("@department", request.Department ?? "---");
//                                    icLab.Parameters.AddWithValue("@year", request.Year ?? "---");
//                                    icLab.Parameters.AddWithValue("@semester", request.Semester ?? "---");
//                                    icLab.Parameters.AddWithValue("@section", request.Section ?? "---");
//                                    icLab.Parameters.AddWithValue("@day", t.Day ?? "---");
//                                    icLab.Parameters.AddWithValue("@hour", h);
//                                    await icLab.ExecuteNonQueryAsync();
//                                }
//                            }
//                        }
//                    }
//                    await tran.CommitAsync();
//                }

//                // Prepare response schedule view for client
//                var responseTimetable = timetable
//                    .Select(row => new
//                    {
//                        Day = row.Key,
//                        HourlySlots = row.Value
//                    }).ToList();

//                return Ok(new
//                {
//                    message = "✅ Timetable generated perfectly with deterministic Backtracking + Constraint Propagation (AC-3).",
//                    timetable = responseTimetable,
//                    usedLabIds = tasks.Where(x => x.IsLab && !string.IsNullOrEmpty(x.LabId)).Select(x => x.LabId).Distinct().ToList(),
//                    receivedPayload = request
//                });
//            }
//            catch (Exception ex)
//            {
//                return StatusCode(500, new { message = "❌ Internal Server Error while generating timetable.", error = ex.Message });
//            }
//        }
//    }
//}







//non perfect with shuffle
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
//            public bool IsPlaced = false;
//            public List<(string day, int start)> Domain = new();
//        }

//        // Random helper extension for shuffling lists
//        private static readonly Random rng = new Random();

//        private void Shuffle<T>(IList<T> list)
//        {
//            int n = list.Count;
//            while (n > 1)
//            {
//                n--;
//                int k = rng.Next(n + 1);
//                (list[k], list[n]) = (list[n], list[k]);
//            }
//        }

//        [HttpPost("generateCrossDepartmentTimetableBacktracking")]
//        public async Task<IActionResult> GenerateCrossDepartmentTimetableBacktracking([FromBody] TimetableRequest request)
//        {
//            var cs = _configuration.GetConnectionString("DefaultConnection");
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

//                using var conn = new NpgsqlConnection(cs);
//                await conn.OpenAsync();

//                var staffOcc = new Dictionary<string, Dictionary<string, HashSet<int>>>();
//                var labOcc = new Dictionary<string, Dictionary<string, HashSet<int>>>();

//                void EnsureDayMap<TKey>(Dictionary<TKey, Dictionary<string, HashSet<int>>> map, TKey key)
//                {
//                    if (!map.ContainsKey(key))
//                        map[key] = DAYS.ToDictionary(d => d, d => new HashSet<int>());
//                }

//                // Load existing staff occupancy
//                using (var cmd = new NpgsqlCommand("SELECT staff_code, day, hour FROM classtimetable", conn))
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

//                // Load existing lab occupancy
//                using (var cmd = new NpgsqlCommand("SELECT lab_id, day, hour FROM labtimetable", conn))
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

//                foreach (var s in subjects)
//                {
//                    var (_, staffCode) = SplitStaff(s.staff);
//                    EnsureDayMap(staffOcc, staffCode);
//                    if (!string.IsNullOrEmpty(s.labId)) EnsureDayMap(labOcc, s.labId);
//                }

//                var tasks = new List<TaskUnit>();
//                var labAllowedBlocks = new List<int[]> { new[] { 1, 2, 3, 4 }, new[] { 4, 5, 6, 7 } };
//                var embeddedLabAllowedBlocks = new List<int[]> {
//                    new[] { 1, 2 },
//                    new[] { 2, 3 },
//                    new[] { 3, 4 },
//                    new[] { 5, 6 },
//                    new[] { 6, 7 }
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
//                                IsLab = false,
//                                Duration = 1,
//                                Kind = "EMB_TH1"
//                            });
//                            tasks.Add(new TaskUnit
//                            {
//                                SubjectCode = s.code,
//                                SubjectName = s.name,
//                                StaffAssigned = s.staff,
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
//                                    IsLab = false,
//                                    Duration = 1,
//                                    Kind = "TH1"
//                                });
//                            }
//                            break;
//                    }
//                }

//                var timetable = DAYS.ToDictionary(d => d,
//                    d => Enumerable.Range(1, HOURS).ToDictionary(h => h, _ => "---"));

//                bool IsValidStartForLab(TaskUnit t, int start)
//                {
//                    if (t.Kind == "LAB4")
//                        return labAllowedBlocks.Any(b => b[0] == start && b.Length == t.Duration);
//                    if (t.Kind == "EMB_LAB2")
//                        return embeddedLabAllowedBlocks.Any(b => b[0] == start && b.Length == t.Duration);
//                    return true;
//                }

//                bool IsFreeAndNoConflict(TaskUnit t, string day, int start,
//                    Dictionary<string, Dictionary<string, HashSet<int>>> staffOccMap,
//                    Dictionary<string, Dictionary<string, HashSet<int>>> labOccMap)
//                {
//                    for (int h = start; h < start + t.Duration; h++)
//                    {
//                        if (h < 1 || h > HOURS) return false;
//                        if (timetable[day][h] != "---") return false;
//                    }
//                    var (_, staffCode) = SplitStaff(t.StaffAssigned);
//                    for (int h = start; h < start + t.Duration; h++)
//                    {
//                        if (staffOccMap.TryGetValue(staffCode, out var dayMap) && dayMap.TryGetValue(day, out var hoursSet) && hoursSet.Contains(h))
//                            return false;
//                    }
//                    if (t.IsLab && !string.IsNullOrEmpty(t.LabId))
//                    {
//                        for (int h = start; h < start + t.Duration; h++)
//                        {
//                            if (labOccMap.TryGetValue(t.LabId, out var labDayMap) && labDayMap.TryGetValue(day, out var hoursSet) && hoursSet.Contains(h))
//                                return false;
//                        }
//                        if (!IsValidStartForLab(t, start))
//                            return false;
//                    }
//                    return true;
//                }

//                // SHUFFLE: Randomize task order before domain initialization for varied schedules
//                Shuffle(tasks);

//                // Initialize domains and shuffle each to add randomness
//                foreach (var t in tasks)
//                {
//                    t.Domain.Clear();
//                    foreach (var day in DAYS)
//                    {
//                        for (int h = 1; h <= HOURS - t.Duration + 1; h++)
//                        {
//                            if (IsFreeAndNoConflict(t, day, h, staffOcc, labOcc))
//                            {
//                                t.Domain.Add((day, h));
//                            }
//                        }
//                    }
//                    if (t.Domain.Count == 0)
//                    {
//                        return Ok(new
//                        {
//                            message = $"⚠ No feasible slot initially for task {t.SubjectCode} {t.SubjectName}. Cannot generate timetable.",
//                            receivedPayload = request
//                        });
//                    }
//                    Shuffle(t.Domain); // SHUFFLE domain before assignment attempts
//                }

//                void AssignTask(TaskUnit t, (string day, int start) slot)
//                {
//                    var (day, start) = slot;
//                    var (_, staffCode) = SplitStaff(t.StaffAssigned);
//                    for (int h = start; h < start + t.Duration; h++)
//                    {
//                        timetable[day][h] = $"{t.SubjectCode} ({t.StaffAssigned})";
//                        if (!staffOcc.ContainsKey(staffCode)) staffOcc[staffCode] = new Dictionary<string, HashSet<int>>();
//                        if (!staffOcc[staffCode].ContainsKey(day)) staffOcc[staffCode][day] = new HashSet<int>();
//                        staffOcc[staffCode][day].Add(h);
//                        if (t.IsLab && !string.IsNullOrEmpty(t.LabId))
//                        {
//                            if (!labOcc.ContainsKey(t.LabId)) labOcc[t.LabId] = new Dictionary<string, HashSet<int>>();
//                            if (!labOcc[t.LabId].ContainsKey(day)) labOcc[t.LabId][day] = new HashSet<int>();
//                            labOcc[t.LabId][day].Add(h);
//                        }
//                    }
//                    t.Day = day;
//                    t.StartHour = start;
//                    t.IsPlaced = true;
//                }

//                void UnassignTask(TaskUnit t)
//                {
//                    if (!t.IsPlaced) return;
//                    var day = t.Day;
//                    var start = t.StartHour;
//                    var (_, staffCode) = SplitStaff(t.StaffAssigned);
//                    for (int h = start; h < start + t.Duration; h++)
//                    {
//                        timetable[day][h] = "---";
//                        staffOcc[staffCode][day].Remove(h);
//                        if (t.IsLab && !string.IsNullOrEmpty(t.LabId))
//                        {
//                            labOcc[t.LabId][day].Remove(h);
//                        }
//                    }
//                    t.IsPlaced = false;
//                    t.Day = null;
//                    t.StartHour = 0;
//                }

//                bool PropagateConstraints(List<TaskUnit> tasksList, TaskUnit assignedTask, (string day, int start) assignedSlot)
//                {
//                    var snapshotDomains = tasksList.ToDictionary(t => t, t => t.Domain.ToList());
//                    assignedTask.Domain = new List<(string, int)> { assignedSlot };
//                    foreach (var other in tasksList)
//                    {
//                        if (other == assignedTask || other.IsPlaced)
//                            continue;
//                        var filteredDomain = new List<(string day, int start)>();
//                        foreach (var pos in other.Domain)
//                        {
//                            bool conflict = false;
//                            if (pos.day == assignedSlot.day)
//                            {
//                                int start1 = pos.start;
//                                int end1 = pos.start + other.Duration - 1;
//                                int start2 = assignedSlot.start;
//                                int end2 = assignedSlot.start + assignedTask.Duration - 1;
//                                bool hoursOverlap = end1 >= start2 && end2 >= start1;
//                                if (hoursOverlap)
//                                {
//                                    var (_, staffCode1) = SplitStaff(other.StaffAssigned);
//                                    var (_, staffCode2) = SplitStaff(assignedTask.StaffAssigned);
//                                    if (staffCode1 == staffCode2) conflict = true;
//                                    if (!conflict && other.IsLab && assignedTask.IsLab && !string.IsNullOrEmpty(other.LabId) && !string.IsNullOrEmpty(assignedTask.LabId))
//                                    {
//                                        if (other.LabId == assignedTask.LabId) conflict = true;
//                                    }
//                                }
//                            }
//                            if (!conflict)
//                            {
//                                filteredDomain.Add(pos);
//                            }
//                        }
//                        other.Domain = filteredDomain;
//                        if (other.Domain.Count == 0)
//                        {
//                            foreach (var kvp in snapshotDomains)
//                            {
//                                kvp.Key.Domain = kvp.Value;
//                            }
//                            return false;
//                        }
//                    }
//                    return true;
//                }

//                bool Backtrack(List<TaskUnit> tasksList)
//                {
//                    if (tasksList.All(t => t.IsPlaced)) return true;
//                    var unassignedTasks = tasksList.Where(t => !t.IsPlaced).OrderBy(t => t.Domain.Count).ToList();
//                    if (unassignedTasks.Count == 0) return true;
//                    var nextTask = unassignedTasks.First();

//                    // Optional: shuffle domain again here for extra randomness
//                    Shuffle(nextTask.Domain);

//                    foreach (var slot in nextTask.Domain)
//                    {
//                        if (IsFreeAndNoConflict(nextTask, slot.day, slot.start, staffOcc, labOcc))
//                        {
//                            AssignTask(nextTask, slot);
//                            bool consistent = PropagateConstraints(tasksList, nextTask, slot);
//                            if (consistent)
//                            {
//                                if (Backtrack(tasksList))
//                                    return true;
//                            }
//                            UnassignTask(nextTask);
//                        }
//                    }
//                    return false;
//                }

//                bool solved = Backtrack(tasks);
//                if (!solved)
//                {
//                    return Ok(new
//                    {
//                        message = "⚠ No solution found with constraint propagation and backtracking, timetable generation failed.",
//                        receivedPayload = request
//                    });
//                }

//                using (var tran = conn.BeginTransaction())
//                {
//                    using (var delClass = new NpgsqlCommand(@"
//                        DELETE FROM classtimetable 
//                        WHERE department_id=@department AND year=@year AND semester=@semester AND section=@section;", conn, tran))
//                    {
//                        delClass.Parameters.AddWithValue("@department", request.Department ?? "---");
//                        delClass.Parameters.AddWithValue("@year", request.Year ?? "---");
//                        delClass.Parameters.AddWithValue("@semester", request.Semester ?? "---");
//                        delClass.Parameters.AddWithValue("@section", request.Section ?? "---");
//                        await delClass.ExecuteNonQueryAsync();
//                    }
//                    using (var delLab = new NpgsqlCommand(@"
//                        DELETE FROM labtimetable
//                        WHERE department=@department AND year=@year AND semester=@semester AND section=@section;", conn, tran))
//                    {
//                        delLab.Parameters.AddWithValue("@department", request.Department ?? "---");
//                        delLab.Parameters.AddWithValue("@year", request.Year ?? "---");
//                        delLab.Parameters.AddWithValue("@semester", request.Semester ?? "---");
//                        delLab.Parameters.AddWithValue("@section", request.Section ?? "---");
//                        await delLab.ExecuteNonQueryAsync();
//                    }

//                    foreach (var t in tasks)
//                    {
//                        var (staffName, staffCode) = SplitStaff(t.StaffAssigned);
//                        for (int h = t.StartHour; h < t.StartHour + t.Duration; h++)
//                        {
//                            using (var icClass = new NpgsqlCommand(@"
//                                INSERT INTO classtimetable
//                                (staff_name, staff_code, department_id, year, semester, section, day, hour, subject_code, subject_name)
//                                VALUES
//                                (@staff_name, @staff_code, @department, @year, @semester, @section, @day, @hour, @subject_code, @subject_name);", conn, tran))
//                            {
//                                icClass.Parameters.AddWithValue("@staff_name", staffName ?? "---");
//                                icClass.Parameters.AddWithValue("@staff_code", staffCode ?? "---");
//                                icClass.Parameters.AddWithValue("@department", request.Department ?? "---");
//                                icClass.Parameters.AddWithValue("@year", request.Year ?? "---");
//                                icClass.Parameters.AddWithValue("@semester", request.Semester ?? "---");
//                                icClass.Parameters.AddWithValue("@section", request.Section ?? "---");
//                                icClass.Parameters.AddWithValue("@day", t.Day ?? "---");
//                                icClass.Parameters.AddWithValue("@hour", h);
//                                icClass.Parameters.AddWithValue("@subject_code", t.SubjectCode ?? "---");
//                                icClass.Parameters.AddWithValue("@subject_name", t.SubjectName ?? "---");
//                                await icClass.ExecuteNonQueryAsync();
//                            }
//                        }
//                        if (t.IsLab && !string.IsNullOrEmpty(t.LabId))
//                        {
//                            int labStart = t.StartHour;
//                            if (labStart == 4)
//                                labStart = 5;
//                            for (int h = labStart; h < t.StartHour + t.Duration; h++)
//                            {
//                                using (var icLab = new NpgsqlCommand(@"
//                                    INSERT INTO labtimetable
//                                    (lab_id, subject_code, subject_name, staff_name, department, year, semester, section, day, hour)
//                                    VALUES
//                                    (@lab_id, @subject_code, @subject_name, @staff_name, @department, @year, @semester, @section, @day, @hour);", conn, tran))
//                                {
//                                    icLab.Parameters.AddWithValue("@lab_id", t.LabId ?? (object)DBNull.Value);
//                                    icLab.Parameters.AddWithValue("@subject_code", t.SubjectCode ?? "---");
//                                    icLab.Parameters.AddWithValue("@subject_name", t.SubjectName ?? "---");
//                                    icLab.Parameters.AddWithValue("@staff_name", staffName ?? "---");
//                                    icLab.Parameters.AddWithValue("@department", request.Department ?? "---");
//                                    icLab.Parameters.AddWithValue("@year", request.Year ?? "---");
//                                    icLab.Parameters.AddWithValue("@semester", request.Semester ?? "---");
//                                    icLab.Parameters.AddWithValue("@section", request.Section ?? "---");
//                                    icLab.Parameters.AddWithValue("@day", t.Day ?? "---");
//                                    icLab.Parameters.AddWithValue("@hour", h);
//                                    await icLab.ExecuteNonQueryAsync();
//                                }
//                            }
//                        }
//                    }
//                    await tran.CommitAsync();
//                }

//                var responseTimetable = timetable.Select(row => new
//                {
//                    Day = row.Key,
//                    HourlySlots = row.Value
//                }).ToList();

//                return Ok(new
//                {
//                    message = "✅ Timetable generated with constraint propagation + randomized backtracking successfully.",
//                    timetable = responseTimetable,
//                    usedLabIds = tasks.Where(x => x.IsLab && !string.IsNullOrEmpty(x.LabId)).Select(x => x.LabId).Distinct().ToList(),
//                    receivedPayload = request
//                });
//            }
//            catch (Exception ex)
//            {
//                return StatusCode(500, new { message = "❌ Error generating timetable.", error = ex.Message });
//            }
//        }
//    }
//}










//i think perfect achieved but disappointed

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
//        public TimetableController(IConfiguration configuration)
//        {
//            _configuration = configuration;
//        }

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
//            public bool IsPlaced = false;
//            public List<(string day, int start)> Domain = new();
//        }

//        private static readonly Random rng = new Random();
//        private void Shuffle<T>(IList<T> list)
//        {
//            int n = list.Count;
//            while (n > 1)
//            {
//                n--;
//                int k = rng.Next(n + 1);
//                (list[k], list[n]) = (list[n], list[k]);
//            }
//        }

//        [HttpPost("generateCrossDepartmentTimetableBacktracking")]
//        public async Task<IActionResult> GenerateCrossDepartmentTimetableBacktracking([FromBody] TimetableRequest request)
//        {
//            var cs = _configuration.GetConnectionString("DefaultConnection");
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

//                using var conn = new NpgsqlConnection(cs);
//                await conn.OpenAsync();

//                var staffOcc = new Dictionary<string, Dictionary<string, HashSet<int>>>();
//                var labOcc = new Dictionary<string, Dictionary<string, HashSet<int>>>();
//                var timetableGrid = DAYS.ToDictionary(
//                    d => d,
//                    d => Enumerable.Range(1, HOURS).ToDictionary(h => h, _ => "---"));

//                void EnsureDayMap<TKey>(Dictionary<TKey, Dictionary<string, HashSet<int>>> map, TKey key)
//                {
//                    if (!map.ContainsKey(key))
//                        map[key] = DAYS.ToDictionary(d => d, _ => new HashSet<int>());
//                }

//                // Load existing occupancy data
//                using (var cmd = new NpgsqlCommand("SELECT staff_code, day, hour FROM classtimetable", conn))
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

//                using (var cmd = new NpgsqlCommand("SELECT lab_id, day, hour FROM labtimetable", conn))
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

//                foreach (var s in subjects)
//                {
//                    var (_, staffCode) = SplitStaff(s.staff);
//                    EnsureDayMap(staffOcc, staffCode);
//                    if (!string.IsNullOrEmpty(s.labId)) EnsureDayMap(labOcc, s.labId);
//                }

//                var tasks = new List<TaskUnit>();
//                var labAllowedBlocks = new List<int[]> { new[] { 1, 2, 3, 4 }, new[] { 4, 5, 6, 7 } };
//                var embeddedLabAllowedBlocks = new List<int[]> {
//                    new[] { 1, 2 },
//                    new[] { 2, 3 },
//                    new[] { 3, 4 },
//                    new[] { 5, 6 },
//                    new[] { 6, 7 }
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
//                                IsLab = false,
//                                Duration = 1,
//                                Kind = "EMB_TH1"
//                            });
//                            tasks.Add(new TaskUnit
//                            {
//                                SubjectCode = s.code,
//                                SubjectName = s.name,
//                                StaffAssigned = s.staff,
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
//                                    IsLab = false,
//                                    Duration = 1,
//                                    Kind = "TH1"
//                                });
//                            }
//                            break;
//                    }
//                }

//                bool IsValidLabStart(TaskUnit t, int start)
//                {
//                    if (t.Kind == "LAB4")
//                        return labAllowedBlocks.Any(b => b[0] == start && b.Length == t.Duration);
//                    if (t.Kind == "EMB_LAB2")
//                        return embeddedLabAllowedBlocks.Any(b => b[0] == start && b.Length == t.Duration);
//                    return true;
//                }

//                bool IsFreeAndNoConflict(TaskUnit t, string day, int start,
//                    Dictionary<string, Dictionary<string, HashSet<int>>> staffOcc,
//                    Dictionary<string, Dictionary<string, HashSet<int>>> labOcc)
//                {
//                    for (int h = start; h < start + t.Duration; h++)
//                    {
//                        if (h < 1 || h > HOURS) return false;
//                        if (timetableGrid[day][h] != "---") return false;
//                    }
//                    var (_, staffCode) = SplitStaff(t.StaffAssigned);
//                    for (int h = start; h < start + t.Duration; h++)
//                    {
//                        if (staffOcc.TryGetValue(staffCode, out var dayHours) && dayHours.TryGetValue(day, out var hoursSet) && hoursSet.Contains(h))
//                            return false;
//                    }
//                    if (t.IsLab && !string.IsNullOrEmpty(t.LabId))
//                    {
//                        for (int h = start; h < start + t.Duration; h++)
//                        {
//                            if (labOcc.TryGetValue(t.LabId, out var dayHours) && dayHours.TryGetValue(day, out var hoursSet) && hoursSet.Contains(h))
//                                return false;
//                        }
//                        if (!IsValidLabStart(t, start))
//                            return false;
//                    }
//                    return true;
//                }

//                void AssignTask(TaskUnit t, (string day, int start) slot)
//                {
//                    var (day, start) = slot;
//                    var (_, staffCode) = SplitStaff(t.StaffAssigned);
//                    for (int h = start; h < start + t.Duration; h++)
//                    {
//                        timetableGrid[day][h] = $"{t.SubjectCode} ({t.StaffAssigned})";
//                        if (!staffOcc.ContainsKey(staffCode)) staffOcc[staffCode] = new Dictionary<string, HashSet<int>>();
//                        if (!staffOcc[staffCode].ContainsKey(day)) staffOcc[staffCode][day] = new HashSet<int>();
//                        staffOcc[staffCode][day].Add(h);
//                        if (t.IsLab && !string.IsNullOrEmpty(t.LabId))
//                        {
//                            if (!labOcc.ContainsKey(t.LabId)) labOcc[t.LabId] = new Dictionary<string, HashSet<int>>();
//                            if (!labOcc[t.LabId].ContainsKey(day)) labOcc[t.LabId][day] = new HashSet<int>();
//                            labOcc[t.LabId][day].Add(h);
//                        }
//                    }
//                    t.Day = day;
//                    t.StartHour = start;
//                    t.IsPlaced = true;
//                }

//                void UnassignTask(TaskUnit t)
//                {
//                    if (!t.IsPlaced) return;
//                    var day = t.Day;
//                    var start = t.StartHour;
//                    var (_, staffCode) = SplitStaff(t.StaffAssigned);
//                    for (int h = start; h < start + t.Duration; h++)
//                    {
//                        timetableGrid[day][h] = "---";
//                        staffOcc[staffCode][day].Remove(h);
//                        if (t.IsLab && !string.IsNullOrEmpty(t.LabId))
//                        {
//                            labOcc[t.LabId][day].Remove(h);
//                        }
//                    }
//                    t.IsPlaced = false;
//                    t.Day = null;
//                    t.StartHour = 0;
//                }

//                bool PropagateConstraints(List<TaskUnit> tasksList, TaskUnit assignedTask, (string day, int start) assignedSlot)
//                {
//                    var snapshotDomains = tasksList.ToDictionary(t => t, t => t.Domain.ToList());
//                    assignedTask.Domain = new List<(string, int)> { assignedSlot };
//                    foreach (var other in tasksList)
//                    {
//                        if (other == assignedTask || other.IsPlaced)
//                            continue;

//                        var filteredDomain = new List<(string day, int start)>();
//                        foreach (var pos in other.Domain)
//                        {
//                            bool conflict = false;
//                            if (pos.day == assignedSlot.day)
//                            {
//                                int start1 = pos.start;
//                                int end1 = pos.start + other.Duration - 1;
//                                int start2 = assignedSlot.start;
//                                int end2 = assignedSlot.start + assignedTask.Duration - 1;
//                                bool hoursOverlap = end1 >= start2 && end2 >= start1;
//                                if (hoursOverlap)
//                                {
//                                    var (_, staffCode1) = SplitStaff(other.StaffAssigned);
//                                    var (_, staffCode2) = SplitStaff(assignedTask.StaffAssigned);
//                                    if (staffCode1 == staffCode2) conflict = true;
//                                    if (!conflict && other.IsLab && assignedTask.IsLab && !string.IsNullOrEmpty(other.LabId) && !string.IsNullOrEmpty(assignedTask.LabId))
//                                    {
//                                        if (other.LabId == assignedTask.LabId) conflict = true;
//                                    }
//                                }
//                            }
//                            if (!conflict)
//                                filteredDomain.Add(pos);
//                        }
//                        other.Domain = filteredDomain;
//                        if (other.Domain.Count == 0)
//                        {
//                            foreach (var kvp in snapshotDomains)
//                                kvp.Key.Domain = kvp.Value;
//                            return false;
//                        }
//                    }
//                    return true;
//                }

//                // Shuffle tasks list before domain initialization for randomness
//                Shuffle(tasks);

//                // Initialize domains with shuffled domains for randomness
//                foreach (var t in tasks)
//                {
//                    t.Domain.Clear();
//                    foreach (var day in DAYS)
//                    {
//                        for (int h = 1; h <= HOURS - t.Duration + 1; h++)
//                        {
//                            if (IsFreeAndNoConflict(t, day, h, staffOcc, labOcc))
//                                t.Domain.Add((day, h));
//                        }
//                    }
//                    if (t.Domain.Count == 0)
//                        return Ok(new { message = $"⚠ No feasible slot initially for task {t.SubjectCode} {t.SubjectName}. Cannot generate timetable.", receivedPayload = request });
//                    Shuffle(t.Domain);
//                }

//                // Recursive backtracking with constraint propagation plus global conflict-aware rescheduling
//                async Task<bool> GlobalReschedule(List<TaskUnit> tasksList)
//                {
//                    if (tasksList.All(t => t.IsPlaced)) return true;
//                    var unassignedTasks = tasksList.Where(t => !t.IsPlaced).OrderBy(t => t.Domain.Count).ToList();
//                    if (!unassignedTasks.Any()) return true;
//                    var currentTask = unassignedTasks.First();
//                    Shuffle(currentTask.Domain);

//                    foreach (var slot in currentTask.Domain)
//                    {
//                        if (IsFreeAndNoConflict(currentTask, slot.day, slot.start, staffOcc, labOcc))
//                        {
//                            AssignTask(currentTask, slot);
//                            bool consistent = PropagateConstraints(tasksList, currentTask, slot);
//                            if (consistent)
//                            {
//                                if (await GlobalReschedule(tasksList))
//                                    return true;
//                            }
//                            // Conflict detected: global rescheduling triggered
//                            var conflictingTasks = CollectConflictingTasks(currentTask, tasksList);

//                            var tasksToReschedule = new List<TaskUnit> { currentTask };
//                            foreach (var ct in conflictingTasks)
//                                if (!tasksToReschedule.Contains(ct)) tasksToReschedule.Add(ct);

//                            // Unassign all conflicting tasks
//                            foreach (var t in tasksToReschedule)
//                                UnassignTask(t);

//                            // Rebuild domains for conflict group with shuffle
//                            foreach (var t in tasksToReschedule)
//                            {
//                                t.Domain.Clear();
//                                foreach (var day in DAYS)
//                                {
//                                    for (int h = 1; h <= HOURS - t.Duration + 1; h++)
//                                    {
//                                        if (IsFreeAndNoConflict(t, day, h, staffOcc, labOcc))
//                                            t.Domain.Add((day, h));
//                                    }
//                                }
//                                if (t.Domain.Count == 0)
//                                {
//                                    // Rollback assignments if no domain exists
//                                    foreach (var revertTask in tasksToReschedule)
//                                        if (!revertTask.IsPlaced && revertTask.Day != null)
//                                            AssignTask(revertTask, (revertTask.Day, revertTask.StartHour));
//                                    continue;
//                                }
//                                Shuffle(t.Domain);
//                            }

//                            if (await GlobalReschedule(tasksToReschedule))
//                                return true;
//                        }
//                    }
//                    return false;
//                }

//                List<TaskUnit> CollectConflictingTasks(TaskUnit task, List<TaskUnit> allTasks)
//                {
//                    var conflicts = new List<TaskUnit>();
//                    var (_, staffCode) = SplitStaff(task.StaffAssigned);

//                    foreach (var other in allTasks)
//                    {
//                        if (other == task || !other.IsPlaced) continue;
//                        bool overlap = other.Day == task.Day && !(other.StartHour + other.Duration <= task.StartHour || task.StartHour + task.Duration <= other.StartHour);
//                        if (overlap)
//                        {
//                            var (_, otherStaffCode) = SplitStaff(other.StaffAssigned);
//                            if (otherStaffCode == staffCode ||
//                                (!string.IsNullOrEmpty(task.LabId) && !string.IsNullOrEmpty(other.LabId) && task.LabId == other.LabId))
//                                conflicts.Add(other);
//                        }
//                    }
//                    return conflicts;
//                }

//                bool solved = await GlobalReschedule(tasks);
//                if (!solved)
//                {
//                    return Ok(new
//                    {
//                        message = "⚠ No solution found with global rescheduling, timetable generation failed.",
//                        receivedPayload = request
//                    });
//                }

//                // Transactionally write to DB
//                using (var tran = conn.BeginTransaction())
//                {
//                    using (var delClass = new NpgsqlCommand(@"
//                        DELETE FROM classtimetable 
//                        WHERE department_id=@department AND year=@year AND semester=@semester AND section=@section;", conn, tran))
//                    {
//                        delClass.Parameters.AddWithValue("@department", request.Department ?? "---");
//                        delClass.Parameters.AddWithValue("@year", request.Year ?? "---");
//                        delClass.Parameters.AddWithValue("@semester", request.Semester ?? "---");
//                        delClass.Parameters.AddWithValue("@section", request.Section ?? "---");
//                        await delClass.ExecuteNonQueryAsync();
//                    }
//                    using (var delLab = new NpgsqlCommand(@"
//                        DELETE FROM labtimetable
//                        WHERE department=@department AND year=@year AND semester=@semester AND section=@section;", conn, tran))
//                    {
//                        delLab.Parameters.AddWithValue("@department", request.Department ?? "---");
//                        delLab.Parameters.AddWithValue("@year", request.Year ?? "---");
//                        delLab.Parameters.AddWithValue("@semester", request.Semester ?? "---");
//                        delLab.Parameters.AddWithValue("@section", request.Section ?? "---");
//                        await delLab.ExecuteNonQueryAsync();
//                    }
//                    foreach (var t in tasks)
//                    {
//                        var (staffName, staffCode) = SplitStaff(t.StaffAssigned);
//                        for (int h = t.StartHour; h < t.StartHour + t.Duration; h++)
//                        {
//                            using (var icClass = new NpgsqlCommand(@"
//                                INSERT INTO classtimetable
//                                (staff_name, staff_code, department_id, year, semester, section, day, hour, subject_code, subject_name)
//                                VALUES
//                                (@staff_name, @staff_code, @department, @year, @semester, @section, @day, @hour, @subject_code, @subject_name);", conn, tran))
//                            {
//                                icClass.Parameters.AddWithValue("@staff_name", staffName ?? "---");
//                                icClass.Parameters.AddWithValue("@staff_code", staffCode ?? "---");
//                                icClass.Parameters.AddWithValue("@department", request.Department ?? "---");
//                                icClass.Parameters.AddWithValue("@year", request.Year ?? "---");
//                                icClass.Parameters.AddWithValue("@semester", request.Semester ?? "---");
//                                icClass.Parameters.AddWithValue("@section", request.Section ?? "---");
//                                icClass.Parameters.AddWithValue("@day", t.Day ?? "---");
//                                icClass.Parameters.AddWithValue("@hour", h);
//                                icClass.Parameters.AddWithValue("@subject_code", t.SubjectCode ?? "---");
//                                icClass.Parameters.AddWithValue("@subject_name", t.SubjectName ?? "---");
//                                await icClass.ExecuteNonQueryAsync();
//                            }
//                        }
//                        if (t.IsLab && !string.IsNullOrEmpty(t.LabId))
//                        {
//                            int labStart = t.StartHour == 4 ? 5 : t.StartHour;
//                            for (int h = labStart; h < t.StartHour + t.Duration; h++)
//                            {
//                                using (var icLab = new NpgsqlCommand(@"
//                                    INSERT INTO labtimetable
//                                    (lab_id, subject_code, subject_name, staff_name, department, year, semester, section, day, hour)
//                                    VALUES
//                                    (@lab_id, @subject_code, @subject_name, @staff_name, @department, @year, @semester, @section, @day, @hour);", conn, tran))
//                                {
//                                    icLab.Parameters.AddWithValue("@lab_id", t.LabId ?? (object)DBNull.Value);
//                                    icLab.Parameters.AddWithValue("@subject_code", t.SubjectCode ?? "---");
//                                    icLab.Parameters.AddWithValue("@subject_name", t.SubjectName ?? "---");
//                                    icLab.Parameters.AddWithValue("@staff_name", staffName ?? "---");
//                                    icLab.Parameters.AddWithValue("@department", request.Department ?? "---");
//                                    icLab.Parameters.AddWithValue("@year", request.Year ?? "---");
//                                    icLab.Parameters.AddWithValue("@semester", request.Semester ?? "---");
//                                    icLab.Parameters.AddWithValue("@section", request.Section ?? "---");
//                                    icLab.Parameters.AddWithValue("@day", t.Day ?? "---");
//                                    icLab.Parameters.AddWithValue("@hour", h);
//                                    await icLab.ExecuteNonQueryAsync();
//                                }
//                            }
//                        }
//                    }
//                    await tran.CommitAsync();
//                }

//                var responseTimetable = timetableGrid.Select(row => new
//                {
//                    Day = row.Key,
//                    HourlySlots = row.Value
//                }).ToList();

//                return Ok(new
//                {
//                    message = "✅ Timetable generated with global rescheduling and constraint propagation successfully.",
//                    timetable = responseTimetable,
//                    usedLabIds = tasks.Where(x => x.IsLab && !string.IsNullOrEmpty(x.LabId)).Select(x => x.LabId).Distinct().ToList(),
//                    receivedPayload = request
//                });
//            }
//            catch (Exception ex)
//            {
//                return StatusCode(500, new { message = "❌ Error generating timetable.", error = ex.Message });
//            }
//        }
//    }
//}





//first perfect backtrack
//using Microsoft.AspNetCore.Mvc;
//using Microsoft.Extensions.Configuration;
//using Npgsql;
//using System;
//using System.Collections.Generic;
//using System.Diagnostics;
//using System.Linq;
//using System.Threading.Tasks;

//namespace Timetablegenerator.Controllers
//{
//    [ApiController]
//    [Route("api/[controller]")]
//    public class TimetableController : ControllerBase
//    {
//        private readonly IConfiguration _configuration;
//        private static readonly Random rng = new Random();

//        public TimetableController(IConfiguration configuration)
//        {
//            _configuration = configuration;
//        }

//        // Collect all related tasks in conflict (sharing staff or lab with the current task) from the database
//        private async Task<List<TaskUnit>> CollectRelatedTasksAsync(
//        TaskUnit task,
//        NpgsqlConnection conn,
//        TimetableRequest request,
//        Dictionary<string, Dictionary<string, HashSet<int>>> staffOcc,
//        Dictionary<string, Dictionary<string, HashSet<int>>> labOcc)
//        {
//            var relatedTasks = new List<TaskUnit>();

//            var involvedStaffIds = new HashSet<string>();
//            var involvedLabIds = new HashSet<string>();

//            var (_, staffCode) = SplitStaff(task.StaffAssigned);
//            involvedStaffIds.Add(staffCode);
//            if (!string.IsNullOrEmpty(task.LabId)) involvedLabIds.Add(task.LabId);

//            // First, get all class timetable entries for involved staff
//            string classSql = @"
//        SELECT staff_code, subject_code, subject_name, day, hour
//        FROM classtimetable
//        WHERE staff_code = ANY(@staffIds)
//          AND year = @year AND semester = @sem AND department_id = @dept AND section = @section
//        ORDER BY day, hour";

//            // Second, get all lab timetable entries for involved labs and staff
//            string labSql = @"
//        SELECT staff_code, subject_code, subject_name, day, hour, lab_id
//        FROM labtimetable
//        WHERE (lab_id = ANY(@labIds) OR staff_code = ANY(@staffIds))
//          AND year = @year AND semester = @sem AND department = @dept AND section = @section
//        ORDER BY day, hour";

//            var entries = new List<(string staff_code, string subject_code, string subject_name, string day, int hour, string lab_id)>();

//            // Query classtable
//            using (var cmd = new NpgsqlCommand(classSql, conn))
//            {
//                cmd.Parameters.AddWithValue("staffIds", involvedStaffIds.ToArray());
//                cmd.Parameters.AddWithValue("year", request.Year);
//                cmd.Parameters.AddWithValue("sem", request.Semester);
//                cmd.Parameters.AddWithValue("dept", request.Department);
//                cmd.Parameters.AddWithValue("section", request.Section);

//                using var reader = await cmd.ExecuteReaderAsync();
//                while (await reader.ReadAsync())
//                {
//                    entries.Add((
//                        reader.GetString(0),
//                        reader.GetString(1),
//                        reader.GetString(2),
//                        reader.GetString(3),
//                        reader.GetInt32(4),
//                        null  // classtable has no lab_id
//                    ));
//                }
//                reader.Close();
//            }

//            // Query labtimetable
//            using (var cmd = new NpgsqlCommand(labSql, conn))
//            {
//                cmd.Parameters.AddWithValue("labIds", involvedLabIds.Count > 0 ? involvedLabIds.ToArray() : new string[] { "" });
//                cmd.Parameters.AddWithValue("staffIds", involvedStaffIds.ToArray());
//                cmd.Parameters.AddWithValue("year", request.Year);
//                cmd.Parameters.AddWithValue("sem", request.Semester);
//                cmd.Parameters.AddWithValue("dept", request.Department);
//                cmd.Parameters.AddWithValue("section", request.Section);

//                using var reader = await cmd.ExecuteReaderAsync();
//                while (await reader.ReadAsync())
//                {
//                    entries.Add((
//                        reader.GetString(0),
//                        reader.GetString(1),
//                        reader.GetString(2),
//                        reader.GetString(3),
//                        reader.GetInt32(4),
//                        reader.IsDBNull(5) ? null : reader.GetString(5)
//                    ));
//                }
//                reader.Close();
//            }

//            // Group entries by subject, staff, day, lab to form continuous task blocks
//            var grouped = entries.GroupBy(e => (e.subject_code, e.staff_code, e.lab_id, e.day));

//            foreach (var group in grouped)
//            {
//                var hours = group.Select(e => e.hour).OrderBy(h => h).ToList();
//                int start = hours.First();
//                int duration = hours.Count;

//                relatedTasks.Add(new TaskUnit
//                {
//                    SubjectCode = group.Key.subject_code,
//                    StaffAssigned = group.Key.staff_code,
//                    SubjectName = group.First().subject_name,
//                    LabId = group.Key.lab_id,
//                    IsLab = !string.IsNullOrEmpty(group.Key.lab_id),
//                    Duration = duration,
//                    Kind = !string.IsNullOrEmpty(group.Key.lab_id) ? "LAB" : "TH",
//                    Day = group.Key.day,
//                    StartHour = start,
//                    IsPlaced = true
//                });
//            }

//            return relatedTasks;
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
//            public bool IsPlaced = false;
//            public List<(string day, int start)> Domain = new();
//        }

//        // Shuffle helper
//        private void Shuffle<T>(IList<T> list)
//        {
//            int n = list.Count;
//            while (n > 1)
//            {
//                n--;
//                int k = rng.Next(n + 1);
//                (list[k], list[n]) = (list[n], list[k]);
//            }
//        }

//        // Staff assignment parser to get code
//        (string staffName, string staffCode) SplitStaff(string staffAssigned)
//        {
//            if (string.IsNullOrWhiteSpace(staffAssigned)) return ("---", "---");
//            var name = staffAssigned;
//            var code = staffAssigned;
//            if (staffAssigned.Contains("("))
//            {
//                var parts = staffAssigned.Split('(', StringSplitOptions.TrimEntries);
//                name = parts[0].Trim();
//                code = parts.Length > 1 ? parts[1].Replace(")", "").Trim() : "---";
//            }
//            return (name, code);
//        }

//        // Main API method
//        [HttpPost("generateCrossDepartmentTimetableBacktracking")]
//        public async Task<IActionResult> GenerateTimetableWithGARescheduling([FromBody] TimetableRequest request)
//        {
//            var cs = _configuration.GetConnectionString("DefaultConnection");
//            await using var conn = new NpgsqlConnection(cs);
//            await conn.OpenAsync();

//            string[] DAYS = new[] { "Mon", "Tue", "Wed", "Thu", "Fri" };
//            int HOURS = Math.Max(1, request.TotalHoursPerDay);
//            var timetableGrid = DAYS.ToDictionary(d => d, d => Enumerable.Range(1, HOURS).ToDictionary(h => h, _ => "---"));
//            var staffOcc = new Dictionary<string, Dictionary<string, HashSet<int>>>();
//            var labOcc = new Dictionary<string, Dictionary<string, HashSet<int>>>();

//            void EnsureDayMap<TKey>(Dictionary<TKey, Dictionary<string, HashSet<int>>> map, TKey key)
//            {
//                if (!map.ContainsKey(key))
//                    map[key] = DAYS.ToDictionary(d => d, _ => new HashSet<int>());
//            }

//            // Load existing staff occupancy
//            await using (var cmd = new NpgsqlCommand("SELECT staff_code, day, hour FROM classtimetable", conn))
//            await using (var rd = await cmd.ExecuteReaderAsync())
//            {
//                while (await rd.ReadAsync())
//                {
//                    var sc = rd["staff_code"] as string ?? "---";
//                    var day = rd["day"] as string ?? "Mon";
//                    var hr = (int)rd["hour"];
//                    EnsureDayMap(staffOcc, sc);
//                    staffOcc[sc][day].Add(hr);
//                }
//            }

//            // Load existing lab occupancy
//            await using (var cmd = new NpgsqlCommand("SELECT lab_id, day, hour FROM labtimetable", conn))
//            await using (var rd = await cmd.ExecuteReaderAsync())
//            {
//                while (await rd.ReadAsync())
//                {
//                    var lab = rd["lab_id"] as string ?? "---";
//                    var day = rd["day"] as string ?? "Mon";
//                    var hr = (int)rd["hour"];
//                    EnsureDayMap(labOcc, lab);
//                    labOcc[lab][day].Add(hr);
//                }
//            }

//            var subjects = request.Subjects?.Where(s => !string.IsNullOrWhiteSpace(s.StaffAssigned)).ToList();
//            if (subjects == null || subjects.Count == 0)
//                return BadRequest(new { message = "❌ No valid subjects with assigned staff found." });

//            var tasks = new List<TaskUnit>();

//            foreach (var s in subjects)
//            {
//                var type = (s.SubjectType ?? "theory").Trim().ToLower();
//                switch (type)
//                {
//                    case "lab":
//                        tasks.Add(new TaskUnit
//                        {
//                            SubjectCode = s.SubjectCode ?? "---",
//                            SubjectName = s.SubjectName ?? "---",
//                            StaffAssigned = s.StaffAssigned,
//                            LabId = s.LabId,
//                            IsLab = true,
//                            Duration = 4,
//                            Kind = "LAB4"
//                        });
//                        break;
//                    case "embedded":
//                        tasks.Add(new TaskUnit
//                        {
//                            SubjectCode = s.SubjectCode ?? "---",
//                            SubjectName = s.SubjectName ?? "---",
//                            StaffAssigned = s.StaffAssigned,
//                            LabId = s.LabId,
//                            IsLab = true,
//                            Duration = 2,
//                            Kind = "EMB_LAB2"
//                        });
//                        tasks.Add(new TaskUnit
//                        {
//                            SubjectCode = s.SubjectCode ?? "---",
//                            SubjectName = s.SubjectName ?? "---",
//                            StaffAssigned = s.StaffAssigned,
//                            IsLab = false,
//                            Duration = 1,
//                            Kind = "EMB_TH1"
//                        });
//                        tasks.Add(new TaskUnit
//                        {
//                            SubjectCode = s.SubjectCode ?? "---",
//                            SubjectName = s.SubjectName ?? "---",
//                            StaffAssigned = s.StaffAssigned,
//                            IsLab = false,
//                            Duration = 1,
//                            Kind = "EMB_TH1"
//                        });
//                        break;
//                    default:
//                        int count = Math.Max(0, s.Credit);
//                        for (int i = 0; i < count; i++)
//                        {
//                            tasks.Add(new TaskUnit
//                            {
//                                SubjectCode = s.SubjectCode ?? "---",
//                                SubjectName = s.SubjectName ?? "---",
//                                StaffAssigned = s.StaffAssigned,
//                                IsLab = false,
//                                Duration = 1,
//                                Kind = "TH1"
//                            });
//                        }
//                        break;
//                }
//            }

//            // Shuffle tasks for randomness
//            Shuffle(tasks);

//            // Initialize domain for each task
//            foreach (var t in tasks)
//            {
//                t.Domain.Clear();
//                foreach (var day in DAYS)
//                {
//                    for (int start = 1; start <= HOURS - t.Duration + 1; start++)
//                    {
//                        if (IsFreeAndNoConflict(t, day, start, staffOcc, labOcc, timetableGrid))
//                        {
//                            t.Domain.Add((day, start));
//                        }
//                    }
//                }
//                if (t.Domain.Count == 0)
//                {
//                    return Ok(new { message = $"No initial available slot for task {t.SubjectCode}.", receivedPayload = request });
//                }
//                Shuffle(t.Domain);
//            }

//            // Recursive backtracking with global conflict handling and GA fallback
//            async Task<bool> BacktrackWithGA(List<TaskUnit> taskList)
//            {
//                if (taskList.All(t => t.IsPlaced)) return true;

//                var currentTask = taskList.Where(t => !t.IsPlaced).OrderBy(t => t.Domain.Count).FirstOrDefault();
//                if (currentTask == null) return true;

//                Shuffle(currentTask.Domain);

//                foreach (var slot in currentTask.Domain)
//                {
//                    if (IsFreeAndNoConflict(currentTask, slot.day, slot.start, staffOcc, labOcc, timetableGrid))
//                    {
//                        AssignTask(currentTask, slot, staffOcc, labOcc, timetableGrid);
//                        if (PropagateConstraints(taskList, currentTask, slot))
//                        {
//                            if (await BacktrackWithGA(taskList)) return true;
//                        }

//                        // Conflict resolution with GA fallback
//                        var conflictTasks = await CollectRelatedTasksAsync(currentTask, conn, request, staffOcc, labOcc);

//                        var reassignmentTasks = new List<TaskUnit> { currentTask };
//                        foreach (var t in conflictTasks)
//                            if (!reassignmentTasks.Contains(t)) reassignmentTasks.Add(t);

//                        foreach (var t in reassignmentTasks)
//                            UnassignTask(t, staffOcc, labOcc, timetableGrid);

//                        var gaResult = await RunGeneticAlgorithmAsync(reassignmentTasks, DAYS, HOURS, staffOcc, labOcc);

//                        if (gaResult.Succeeded)
//                        {
//                            foreach (var t in reassignmentTasks)
//                            {
//                                t.Domain.Clear();
//                                t.Domain.Add((t.Day, t.StartHour));
//                                AssignTask(t, (t.Day, t.StartHour), staffOcc, labOcc, timetableGrid);
//                            }
//                            if (await BacktrackWithGA(taskList)) return true;
//                        }
//                        else
//                        {
//                            foreach (var t in reassignmentTasks)
//                                if (t.Day != null && !t.IsPlaced)
//                                    AssignTask(t, (t.Day, t.StartHour), staffOcc, labOcc, timetableGrid);
//                        }
//                        UnassignTask(currentTask, staffOcc, labOcc, timetableGrid);
//                    }
//                }
//                return false;
//            }

//            // Check if task fits at timeslot without conflicts
//            bool IsFreeAndNoConflict(TaskUnit t, string day, int start,
//                Dictionary<string, Dictionary<string, HashSet<int>>> staffOcc,
//                Dictionary<string, Dictionary<string, HashSet<int>>> labOcc,
//                Dictionary<string, Dictionary<int, string>> timetableGrid)
//            {
//                for (int h = start; h < start + t.Duration; h++)
//                {
//                    if (h < 1 || h > HOURS) return false;
//                    if (timetableGrid[day][h] != "---") return false;
//                }
//                var (_, staffCode) = SplitStaff(t.StaffAssigned);
//                for (int h = start; h < start + t.Duration; h++)
//                {
//                    if (staffOcc.TryGetValue(staffCode, out var dm) && dm.TryGetValue(day, out var set) && set.Contains(h))
//                        return false;
//                }
//                if (t.IsLab && !string.IsNullOrEmpty(t.LabId))
//                {
//                    for (int h = start; h < start + t.Duration; h++)
//                    {
//                        if (labOcc.TryGetValue(t.LabId, out var dm) && dm.TryGetValue(day, out var set) && set.Contains(h))
//                            return false;
//                    }

//                    // Example for lab schedule constraints (4 hour continuous block)
//                    if (t.Kind == "LAB4")
//                    {
//                        if (!(start == 1 || start == 4)) return false;
//                    }
//                }
//                return true;
//            }

//            // Assign task to slot updating data structures
//            void AssignTask(TaskUnit t, (string day, int start) slot,
//                Dictionary<string, Dictionary<string, HashSet<int>>> staffOcc,
//                Dictionary<string, Dictionary<string, HashSet<int>>> labOcc,
//                Dictionary<string, Dictionary<int, string>> timetableGrid)
//            {
//                var (day, start) = slot;
//                var (_, staffCode) = SplitStaff(t.StaffAssigned);
//                for (int h = start; h < start + t.Duration; h++)
//                {
//                    timetableGrid[day][h] = $"{t.SubjectCode} ({t.StaffAssigned})";
//                    if (!staffOcc.ContainsKey(staffCode)) staffOcc[staffCode] = new Dictionary<string, HashSet<int>>();
//                    if (!staffOcc[staffCode].ContainsKey(day)) staffOcc[staffCode][day] = new HashSet<int>();
//                    staffOcc[staffCode][day].Add(h);

//                    if (t.IsLab && !string.IsNullOrEmpty(t.LabId))
//                    {
//                        if (!labOcc.ContainsKey(t.LabId)) labOcc[t.LabId] = new Dictionary<string, HashSet<int>>();
//                        if (!labOcc[t.LabId].ContainsKey(day)) labOcc[t.LabId][day] = new HashSet<int>();
//                        labOcc[t.LabId][day].Add(h);
//                    }
//                }
//                t.Day = day;
//                t.StartHour = start;
//                t.IsPlaced = true;
//            }

//            // Unassign task removing from data structures
//            void UnassignTask(TaskUnit t,
//                Dictionary<string, Dictionary<string, HashSet<int>>> staffOcc,
//                Dictionary<string, Dictionary<string, HashSet<int>>> labOcc,
//                Dictionary<string, Dictionary<int, string>> timetableGrid)
//            {
//                if (!t.IsPlaced) return;
//                var day = t.Day;
//                var start = t.StartHour;
//                var (_, staffCode) = SplitStaff(t.StaffAssigned);
//                for (int h = start; h < start + t.Duration; h++)
//                {
//                    timetableGrid[day][h] = "---";
//                    staffOcc[staffCode][day].Remove(h);
//                    if (t.IsLab && !string.IsNullOrEmpty(t.LabId))
//                        labOcc[t.LabId][day].Remove(h);
//                }
//                t.IsPlaced = false;
//                t.Day = null;
//                t.StartHour = 0;
//            }

//            // Constraint propagation (simple forward checking)
//            bool PropagateConstraints(List<TaskUnit> tasksList, TaskUnit assignedTask, (string day, int start) assignedSlot)
//            {
//                var snapshotDomains = tasksList.ToDictionary(t => t, t => t.Domain.ToList());
//                assignedTask.Domain = new List<(string, int)> { assignedSlot };
//                foreach (var other in tasksList)
//                {
//                    if (other == assignedTask || other.IsPlaced) continue;
//                    var filtered = new List<(string day, int start)>();
//                    foreach (var pos in other.Domain)
//                    {
//                        bool conflict = false;
//                        if (pos.day == assignedSlot.day)
//                        {
//                            int start1 = pos.start;
//                            int end1 = pos.start + other.Duration - 1;
//                            int start2 = assignedSlot.start;
//                            int end2 = assignedSlot.start + assignedTask.Duration - 1;
//                            bool overlap = end1 >= start2 && end2 >= start1;
//                            if (overlap)
//                            {
//                                var (_, staff1) = SplitStaff(other.StaffAssigned);
//                                var (_, staff2) = SplitStaff(assignedTask.StaffAssigned);
//                                if (staff1 == staff2) conflict = true;
//                                if (!conflict && other.IsLab && assignedTask.IsLab &&
//                                    !string.IsNullOrEmpty(other.LabId) && !string.IsNullOrEmpty(assignedTask.LabId) && other.LabId == assignedTask.LabId)
//                                    conflict = true;
//                            }
//                        }
//                        if (!conflict) filtered.Add(pos);
//                    }
//                    other.Domain = filtered;
//                    if (other.Domain.Count == 0)
//                    {
//                        foreach (var kvp in snapshotDomains) kvp.Key.Domain = kvp.Value;
//                        return false;
//                    }
//                }
//                return true;
//            }

//            // Collect all conflicting tasks sharing staff or lab and overlapping times for global rescheduling
//            List<TaskUnit> CollectConflictingTasks(TaskUnit task, List<TaskUnit> allTasks)
//            {
//                var conflicts = new List<TaskUnit>();
//                var (_, staffCode) = SplitStaff(task.StaffAssigned);
//                foreach (var other in allTasks)
//                {
//                    if (other == task || !other.IsPlaced) continue;
//                    bool overlap = other.Day == task.Day && !(other.StartHour + other.Duration <= task.StartHour || task.StartHour + task.Duration <= other.StartHour);
//                    if (overlap)
//                    {
//                        var (_, otherStaffCode) = SplitStaff(other.StaffAssigned);
//                        if (otherStaffCode == staffCode ||
//                            (!string.IsNullOrEmpty(task.LabId) && !string.IsNullOrEmpty(other.LabId) && task.LabId == other.LabId))
//                            conflicts.Add(other);
//                    }
//                }
//                return conflicts;
//            }

//            // Placeholder GA method; replace with your optimizer to solve the subset of tasks conflict-free
//            async Task<(bool Succeeded, List<TaskUnit> Result)> RunGeneticAlgorithmAsync(List<TaskUnit> tasksToAssign,
//                string[] days, int hours,
//                Dictionary<string, Dictionary<string, HashSet<int>>> staffOcc,
//                Dictionary<string, Dictionary<string, HashSet<int>>> labOcc)
//            {
//                // TODO: Implement your GA or Simulated Annealing here to find a feasible schedule for tasksToAssign

//                await Task.Delay(1000); // simulate work

//                // Return failure by default
//                return (false, null);
//            }

//            bool solved = await BacktrackWithGA(tasks);
//            if (!solved)
//                return Ok(new { message = "❌ Could not generate a conflict-free timetable.", receivedPayload = request });

//            // Save to DB atomically
//            await using var transaction = await conn.BeginTransactionAsync();

//            await using (var delClass = new NpgsqlCommand(@"
//            DELETE FROM classtimetable WHERE department_id=@department AND year=@year AND semester=@sem AND section=@section;", conn, transaction))
//            {
//                delClass.Parameters.AddWithValue("department", request.Department ?? "---");
//                delClass.Parameters.AddWithValue("year", request.Year ?? "---");
//                delClass.Parameters.AddWithValue("sem", request.Semester ?? "---");
//                delClass.Parameters.AddWithValue("section", request.Section ?? "---");
//                await delClass.ExecuteNonQueryAsync();
//            }

//            await using (var delLab = new NpgsqlCommand(@"
//            DELETE FROM labtimetable WHERE department=@department AND year=@year AND semester=@sem AND section=@section;", conn, transaction))
//            {
//                delLab.Parameters.AddWithValue("department", request.Department ?? "---");
//                delLab.Parameters.AddWithValue("year", request.Year ?? "---");
//                delLab.Parameters.AddWithValue("sem", request.Semester ?? "---");
//                delLab.Parameters.AddWithValue("section", request.Section ?? "---");
//                await delLab.ExecuteNonQueryAsync();
//            }

//            foreach (var t in tasks)
//            {
//                var (staffName, staffCode) = SplitStaff(t.StaffAssigned);
//                for (int h = t.StartHour; h < t.StartHour + t.Duration; h++)
//                {
//                    await using var icClass = new NpgsqlCommand(@"
//                    INSERT INTO classtimetable
//                    (staff_name, staff_code, department_id, year, semester, section, day, hour, subject_code, subject_name)
//                    VALUES
//                    (@staff_name, @staff_code, @department, @year, @semester, @section, @day, @hour, @subject_code, @subject_name);", conn, transaction);

//                    icClass.Parameters.AddWithValue("@staff_name", staffName ?? "---");
//                    icClass.Parameters.AddWithValue("@staff_code", staffCode ?? "---");
//                    icClass.Parameters.AddWithValue("@department", request.Department ?? "---");
//                    icClass.Parameters.AddWithValue("@year", request.Year ?? "---");
//                    icClass.Parameters.AddWithValue("@semester", request.Semester ?? "---");
//                    icClass.Parameters.AddWithValue("@section", request.Section ?? "---");
//                    icClass.Parameters.AddWithValue("@day", t.Day ?? "---");
//                    icClass.Parameters.AddWithValue("@hour", h);
//                    icClass.Parameters.AddWithValue("@subject_code", t.SubjectCode ?? "---");
//                    icClass.Parameters.AddWithValue("@subject_name", t.SubjectName ?? "---");
//                    await icClass.ExecuteNonQueryAsync();
//                }
//                if (t.IsLab && !string.IsNullOrEmpty(t.LabId))
//                {
//                    int labStart = t.StartHour == 4 ? 5 : t.StartHour;
//                    for (int h = labStart; h < t.StartHour + t.Duration; h++)
//                    {
//                        await using var icLab = new NpgsqlCommand(@"
//                        INSERT INTO labtimetable
//                        (lab_id, subject_code, subject_name, staff_name, department, year, semester, section, day, hour)
//                        VALUES
//                        (@lab_id, @subject_code, @subject_name, @staff_name, @department, @year, @semester, @section, @day, @hour);", conn, transaction);

//                        icLab.Parameters.AddWithValue("@lab_id", t.LabId ?? (object)DBNull.Value);
//                        icLab.Parameters.AddWithValue("@subject_code", t.SubjectCode ?? "---");
//                        icLab.Parameters.AddWithValue("@subject_name", t.SubjectName ?? "---");
//                        icLab.Parameters.AddWithValue("@staff_name", staffName ?? "---");
//                        icLab.Parameters.AddWithValue("@department", request.Department ?? "---");
//                        icLab.Parameters.AddWithValue("@year", request.Year ?? "---");
//                        icLab.Parameters.AddWithValue("@semester", request.Semester ?? "---");
//                        icLab.Parameters.AddWithValue("@section", request.Section ?? "---");
//                        icLab.Parameters.AddWithValue("@day", t.Day ?? "---");
//                        icLab.Parameters.AddWithValue("@hour", h);
//                        await icLab.ExecuteNonQueryAsync();
//                    }
//                }
//            }
//            await transaction.CommitAsync();

//            return Ok(new
//            {
//                message = "✅ Timetable generated successfully with advanced global conflict resolution.",
//                timetable = timetableGrid.Select(t => new { Day = t.Key, Slots = t.Value }),
//                usedLabIds = tasks.Where(t => t.IsLab && !string.IsNullOrEmpty(t.LabId)).Select(t => t.LabId).Distinct(),
//                receivedPayload = request
//            });
//        }
//    }


//}







//second perfect backtrack
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
//        private static readonly Random rng = new Random();

//        public TimetableController(IConfiguration configuration)
//        {
//            _configuration = configuration;
//        }

//        // Collect all related tasks in conflict (sharing staff or lab with the current task) from the database
//        private async Task<List<TaskUnit>> CollectRelatedTasksAsync(
//            TaskUnit task,
//            NpgsqlConnection conn,
//            TimetableRequest request,
//            Dictionary<string, Dictionary<string, HashSet<int>>> staffOcc,
//            Dictionary<string, Dictionary<string, HashSet<int>>> labOcc)
//        {
//            var relatedTasks = new List<TaskUnit>();
//            var involvedStaffIds = new HashSet<string>();
//            var involvedLabIds = new HashSet<string>();
//            var (_, staffCode) = SplitStaff(task.StaffAssigned);
//            involvedStaffIds.Add(staffCode);
//            if (!string.IsNullOrEmpty(task.LabId)) involvedLabIds.Add(task.LabId);
//            string classSql = @"
//        SELECT staff_code, subject_code, subject_name, day, hour
//        FROM classtimetable
//        WHERE staff_code = ANY(@staffIds)
//          AND year = @year AND semester = @sem AND department_id = @dept AND section = @section
//        ORDER BY day, hour";
//            string labSql = @"
//        SELECT staff_code, subject_code, subject_name, day, hour, lab_id
//        FROM labtimetable
//        WHERE (lab_id = ANY(@labIds) OR staff_code = ANY(@staffIds))
//          AND year = @year AND semester = @sem AND department = @dept AND section = @section
//        ORDER BY day, hour";
//            var entries = new List<(string staff_code, string subject_code, string subject_name, string day, int hour, string lab_id)>();
//            using (var cmd = new NpgsqlCommand(classSql, conn))
//            {
//                cmd.Parameters.AddWithValue("staffIds", involvedStaffIds.ToArray());
//                cmd.Parameters.AddWithValue("year", request.Year);
//                cmd.Parameters.AddWithValue("sem", request.Semester);
//                cmd.Parameters.AddWithValue("dept", request.Department);
//                cmd.Parameters.AddWithValue("section", request.Section);
//                using var reader = await cmd.ExecuteReaderAsync();
//                while (await reader.ReadAsync())
//                {
//                    entries.Add((
//                        reader.GetString(0),
//                        reader.GetString(1),
//                        reader.GetString(2),
//                        reader.GetString(3),
//                        reader.GetInt32(4),
//                        null
//                    ));
//                }
//                reader.Close();
//            }
//            using (var cmd = new NpgsqlCommand(labSql, conn))
//            {
//                cmd.Parameters.AddWithValue("labIds", involvedLabIds.Count > 0 ? involvedLabIds.ToArray() : new string[] { "" });
//                cmd.Parameters.AddWithValue("staffIds", involvedStaffIds.ToArray());
//                cmd.Parameters.AddWithValue("year", request.Year);
//                cmd.Parameters.AddWithValue("sem", request.Semester);
//                cmd.Parameters.AddWithValue("dept", request.Department);
//                cmd.Parameters.AddWithValue("section", request.Section);
//                using var reader = await cmd.ExecuteReaderAsync();
//                while (await reader.ReadAsync())
//                {
//                    entries.Add((
//                        reader.GetString(0),
//                        reader.GetString(1),
//                        reader.GetString(2),
//                        reader.GetString(3),
//                        reader.GetInt32(4),
//                        reader.IsDBNull(5) ? null : reader.GetString(5)
//                    ));
//                }
//                reader.Close();
//            }
//            var grouped = entries.GroupBy(e => (e.subject_code, e.staff_code, e.lab_id, e.day));
//            foreach (var group in grouped)
//            {
//                var hours = group.Select(e => e.hour).OrderBy(h => h).ToList();
//                int start = hours.First();
//                int duration = hours.Count;
//                relatedTasks.Add(new TaskUnit
//                {
//                    SubjectCode = group.Key.subject_code,
//                    StaffAssigned = group.Key.staff_code,
//                    SubjectName = group.First().subject_name,
//                    LabId = group.Key.lab_id,
//                    IsLab = !string.IsNullOrEmpty(group.Key.lab_id),
//                    Duration = duration,
//                    Kind = !string.IsNullOrEmpty(group.Key.lab_id) ? "LAB" : "TH",
//                    Day = group.Key.day,
//                    StartHour = start,
//                    IsPlaced = true
//                });
//            }
//            return relatedTasks;
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
//            public bool IsPlaced = false;
//            public List<(string day, int start)> Domain = new();
//        }

//        // Shuffle helper
//        private void Shuffle<T>(IList<T> list)
//        {
//            int n = list.Count;
//            while (n > 1)
//            {
//                n--;
//                int k = rng.Next(n + 1);
//                (list[k], list[n]) = (list[n], list[k]);
//            }
//        }

//        // Staff assignment parser to get code
//        (string staffName, string staffCode) SplitStaff(string staffAssigned)
//        {
//            if (string.IsNullOrWhiteSpace(staffAssigned)) return ("---", "---");
//            var name = staffAssigned;
//            var code = staffAssigned;
//            if (staffAssigned.Contains("("))
//            {
//                var parts = staffAssigned.Split('(', StringSplitOptions.TrimEntries);
//                name = parts[0].Trim();
//                code = parts.Length > 1 ? parts[1].Replace(")", "").Trim() : "---";
//            }
//            return (name, code);
//        }

//        // Main API method
//        [HttpPost("generateCrossDepartmentTimetableBacktracking")]
//        public async Task<IActionResult> GenerateTimetableWithGARescheduling([FromBody] TimetableRequest request)
//        {
//            var cs = _configuration.GetConnectionString("DefaultConnection");
//            await using var conn = new NpgsqlConnection(cs);
//            await conn.OpenAsync();
//            string[] DAYS = new[] { "Mon", "Tue", "Wed", "Thu", "Fri" };
//            int HOURS = Math.Max(1, request.TotalHoursPerDay);
//            var timetableGrid = DAYS.ToDictionary(d => d, d => Enumerable.Range(1, HOURS).ToDictionary(h => h, _ => "---"));
//            var staffOcc = new Dictionary<string, Dictionary<string, HashSet<int>>>();
//            var labOcc = new Dictionary<string, Dictionary<string, HashSet<int>>>();

//            void EnsureDayMap<TKey>(Dictionary<TKey, Dictionary<string, HashSet<int>>> map, TKey key)
//            {
//                if (!map.ContainsKey(key))
//                    map[key] = DAYS.ToDictionary(d => d, _ => new HashSet<int>());
//            }

//            // Load existing staff occupancy
//            await using (var cmd = new NpgsqlCommand("SELECT staff_code, day, hour FROM classtimetable", conn))
//            await using (var rd = await cmd.ExecuteReaderAsync())
//            {
//                while (await rd.ReadAsync())
//                {
//                    var sc = rd["staff_code"] as string ?? "---";
//                    var day = rd["day"] as string ?? "Mon";
//                    var hr = (int)rd["hour"];
//                    EnsureDayMap(staffOcc, sc);
//                    staffOcc[sc][day].Add(hr);
//                }
//            }

//            // Load existing lab occupancy
//            await using (var cmd = new NpgsqlCommand("SELECT lab_id, day, hour FROM labtimetable", conn))
//            await using (var rd = await cmd.ExecuteReaderAsync())
//            {
//                while (await rd.ReadAsync())
//                {
//                    var lab = rd["lab_id"] as string ?? "---";
//                    var day = rd["day"] as string ?? "Mon";
//                    var hr = (int)rd["hour"];
//                    EnsureDayMap(labOcc, lab);
//                    labOcc[lab][day].Add(hr);
//                }
//            }

//            var subjects = request.Subjects?.Where(s => !string.IsNullOrWhiteSpace(s.StaffAssigned)).ToList();
//            if (subjects == null || subjects.Count == 0)
//                return BadRequest(new { message = "❌ No valid subjects with assigned staff found." });

//            var tasks = new List<TaskUnit>();
//            foreach (var s in subjects)
//            {
//                var type = (s.SubjectType ?? "theory").Trim().ToLower();
//                switch (type)
//                {
//                    case "lab":
//                        tasks.Add(new TaskUnit
//                        {
//                            SubjectCode = s.SubjectCode ?? "---",
//                            SubjectName = s.SubjectName ?? "---",
//                            StaffAssigned = s.StaffAssigned,
//                            LabId = s.LabId,
//                            IsLab = true,
//                            Duration = 4,
//                            Kind = "LAB4"
//                        });
//                        break;
//                    case "embedded":
//                        tasks.Add(new TaskUnit
//                        {
//                            SubjectCode = s.SubjectCode ?? "---",
//                            SubjectName = s.SubjectName ?? "---",
//                            StaffAssigned = s.StaffAssigned,
//                            LabId = s.LabId,
//                            IsLab = true,
//                            Duration = 2,
//                            Kind = "EMB_LAB2"
//                        });
//                        tasks.Add(new TaskUnit
//                        {
//                            SubjectCode = s.SubjectCode ?? "---",
//                            SubjectName = s.SubjectName ?? "---",
//                            StaffAssigned = s.StaffAssigned,
//                            IsLab = false,
//                            Duration = 1,
//                            Kind = "EMB_TH1"
//                        });
//                        tasks.Add(new TaskUnit
//                        {
//                            SubjectCode = s.SubjectCode ?? "---",
//                            SubjectName = s.SubjectName ?? "---",
//                            StaffAssigned = s.StaffAssigned,
//                            IsLab = false,
//                            Duration = 1,
//                            Kind = "EMB_TH1"
//                        });
//                        break;
//                    default:
//                        int count = Math.Max(0, s.Credit);
//                        for (int i = 0; i < count; i++)
//                        {
//                            tasks.Add(new TaskUnit
//                            {
//                                SubjectCode = s.SubjectCode ?? "---",
//                                SubjectName = s.SubjectName ?? "---",
//                                StaffAssigned = s.StaffAssigned,
//                                IsLab = false,
//                                Duration = 1,
//                                Kind = "TH1"
//                            });
//                        }
//                        break;
//                }
//            }

//            // Shuffle tasks for randomness
//            Shuffle(tasks);

//            // Initialize domain for each task
//            foreach (var t in tasks)
//            {
//                t.Domain.Clear();
//                foreach (var day in DAYS)
//                {
//                    for (int start = 1; start <= HOURS - t.Duration + 1; start++)
//                    {
//                        if (IsFreeAndNoConflict(t, day, start, staffOcc, labOcc, timetableGrid))
//                        {
//                            t.Domain.Add((day, start));
//                        }
//                    }
//                }
//                if (t.Domain.Count == 0)
//                {
//                    return Ok(new { message = $"No initial available slot for task {t.SubjectCode}.", receivedPayload = request });
//                }
//                Shuffle(t.Domain);
//            }

//            // Recursive backtracking with global conflict handling and GA fallback
//            async Task<bool> BacktrackWithGA(List<TaskUnit> taskList)
//            {
//                if (taskList.All(t => t.IsPlaced)) return true;
//                var currentTask = taskList.Where(t => !t.IsPlaced).OrderBy(t => t.Domain.Count).FirstOrDefault();
//                if (currentTask == null) return true;
//                Shuffle(currentTask.Domain);
//                foreach (var slot in currentTask.Domain)
//                {
//                    if (IsFreeAndNoConflict(currentTask, slot.day, slot.start, staffOcc, labOcc, timetableGrid))
//                    {
//                        AssignTask(currentTask, slot, staffOcc, labOcc, timetableGrid);
//                        if (PropagateConstraints(taskList, currentTask, slot))
//                        {
//                            if (await BacktrackWithGA(taskList)) return true;
//                        }
//                        // Conflict resolution with GA fallback
//                        var conflictTasks = await CollectRelatedTasksAsync(currentTask, conn, request, staffOcc, labOcc);
//                        var reassignmentTasks = new List<TaskUnit> { currentTask };
//                        foreach (var t in conflictTasks)
//                            if (!reassignmentTasks.Contains(t)) reassignmentTasks.Add(t);
//                        foreach (var t in reassignmentTasks)
//                            UnassignTask(t, staffOcc, labOcc, timetableGrid);
//                        var gaResult = await RunGeneticAlgorithmAsync(reassignmentTasks, DAYS, HOURS, staffOcc, labOcc);
//                        if (gaResult.Succeeded)
//                        {
//                            foreach (var t in reassignmentTasks)
//                            {
//                                t.Domain.Clear();
//                                t.Domain.Add((t.Day, t.StartHour));
//                                AssignTask(t, (t.Day, t.StartHour), staffOcc, labOcc, timetableGrid);
//                            }
//                            if (await BacktrackWithGA(taskList)) return true;
//                        }
//                        else
//                        {
//                            foreach (var t in reassignmentTasks)
//                                if (t.Day != null && !t.IsPlaced)
//                                    AssignTask(t, (t.Day, t.StartHour), staffOcc, labOcc, timetableGrid);
//                        }
//                        UnassignTask(currentTask, staffOcc, labOcc, timetableGrid);
//                    }
//                }
//                return false;
//            }

//            bool IsFreeAndNoConflict(TaskUnit t, string day, int start,
//                Dictionary<string, Dictionary<string, HashSet<int>>> staffOcc,
//                Dictionary<string, Dictionary<string, HashSet<int>>> labOcc,
//                Dictionary<string, Dictionary<int, string>> timetableGrid)
//            {
//                for (int h = start; h < start + t.Duration; h++)
//                {
//                    if (h < 1 || h > HOURS) return false;
//                    if (timetableGrid[day][h] != "---") return false;
//                }
//                var (_, staffCode) = SplitStaff(t.StaffAssigned);
//                for (int h = start; h < start + t.Duration; h++)
//                {
//                    if (staffOcc.TryGetValue(staffCode, out var dm) && dm.TryGetValue(day, out var set) && set.Contains(h))
//                        return false;
//                }
//                if (t.IsLab && !string.IsNullOrEmpty(t.LabId))
//                {
//                    for (int h = start; h < start + t.Duration; h++)
//                    {
//                        if (labOcc.TryGetValue(t.LabId, out var dm) && dm.TryGetValue(day, out var set) && set.Contains(h))
//                            return false;
//                    }
//                    if (t.Kind == "LAB4")
//                    {
//                        if (!(start == 1 || start == 4)) return false;
//                    }
//                }
//                return true;
//            }

//            void AssignTask(TaskUnit t, (string day, int start) slot,
//                Dictionary<string, Dictionary<string, HashSet<int>>> staffOcc,
//                Dictionary<string, Dictionary<string, HashSet<int>>> labOcc,
//                Dictionary<string, Dictionary<int, string>> timetableGrid)
//            {
//                var (day, start) = slot;
//                var (_, staffCode) = SplitStaff(t.StaffAssigned);
//                for (int h = start; h < start + t.Duration; h++)
//                {
//                    timetableGrid[day][h] = $"{t.SubjectCode} ({t.StaffAssigned})";
//                    if (!staffOcc.ContainsKey(staffCode)) staffOcc[staffCode] = new Dictionary<string, HashSet<int>>();
//                    if (!staffOcc[staffCode].ContainsKey(day)) staffOcc[staffCode][day] = new HashSet<int>();
//                    staffOcc[staffCode][day].Add(h);
//                    if (t.IsLab && !string.IsNullOrEmpty(t.LabId))
//                    {
//                        if (!labOcc.ContainsKey(t.LabId)) labOcc[t.LabId] = new Dictionary<string, HashSet<int>>();
//                        if (!labOcc[t.LabId].ContainsKey(day)) labOcc[t.LabId][day] = new HashSet<int>();
//                        labOcc[t.LabId][day].Add(h);
//                    }
//                }
//                t.Day = day;
//                t.StartHour = start;
//                t.IsPlaced = true;
//            }

//            void UnassignTask(TaskUnit t,
//                Dictionary<string, Dictionary<string, HashSet<int>>> staffOcc,
//                Dictionary<string, Dictionary<string, HashSet<int>>> labOcc,
//                Dictionary<string, Dictionary<int, string>> timetableGrid)
//            {
//                if (!t.IsPlaced) return;
//                var day = t.Day;
//                var start = t.StartHour;
//                var (_, staffCode) = SplitStaff(t.StaffAssigned);
//                for (int h = start; h < start + t.Duration; h++)
//                {
//                    timetableGrid[day][h] = "---";
//                    staffOcc[staffCode][day].Remove(h);
//                    if (t.IsLab && !string.IsNullOrEmpty(t.LabId))
//                        labOcc[t.LabId][day].Remove(h);
//                }
//                t.IsPlaced = false;
//                t.Day = null;
//                t.StartHour = 0;
//            }

//            bool PropagateConstraints(List<TaskUnit> tasksList, TaskUnit assignedTask, (string day, int start) assignedSlot)
//            {
//                var snapshotDomains = tasksList.ToDictionary(t => t, t => t.Domain.ToList());
//                assignedTask.Domain = new List<(string, int)> { assignedSlot };
//                foreach (var other in tasksList)
//                {
//                    if (other == assignedTask || other.IsPlaced) continue;
//                    var filtered = new List<(string day, int start)>();
//                    foreach (var pos in other.Domain)
//                    {
//                        bool conflict = false;
//                        if (pos.day == assignedSlot.day)
//                        {
//                            int start1 = pos.start;
//                            int end1 = pos.start + other.Duration - 1;
//                            int start2 = assignedSlot.start;
//                            int end2 = assignedSlot.start + assignedTask.Duration - 1;
//                            bool overlap = end1 >= start2 && end2 >= start1;
//                            if (overlap)
//                            {
//                                var (_, staff1) = SplitStaff(other.StaffAssigned);
//                                var (_, staff2) = SplitStaff(assignedTask.StaffAssigned);
//                                if (staff1 == staff2) conflict = true;
//                                if (!conflict && other.IsLab && assignedTask.IsLab &&
//                                    !string.IsNullOrEmpty(other.LabId) && !string.IsNullOrEmpty(assignedTask.LabId) && other.LabId == assignedTask.LabId)
//                                    conflict = true;
//                            }
//                        }
//                        if (!conflict) filtered.Add(pos);
//                    }
//                    other.Domain = filtered;
//                    if (other.Domain.Count == 0)
//                    {
//                        foreach (var kvp in snapshotDomains) kvp.Key.Domain = kvp.Value;
//                        return false;
//                    }
//                }
//                return true;
//            }

//            // RunGeneticAlgorithmAsync: implements GA to find conflict-free assignment of conflicting tasks
//            async Task<(bool Succeeded, List<TaskUnit> Result)> RunGeneticAlgorithmAsync(
//                List<TaskUnit> tasksToAssign,
//                string[] days,
//                int hours,
//                Dictionary<string, Dictionary<string, HashSet<int>>> staffOcc,
//                Dictionary<string, Dictionary<string, HashSet<int>>> labOcc)
//            {
//                const int populationSize = 30;
//                const int maxGenerations = 100;
//                const double mutationRate = 0.1;

//                bool CanPlace(TaskUnit t, string day, int start)
//                {
//                    if (start < 1 || start + t.Duration - 1 > hours)
//                        return false;

//                    var (_, staffCode) = SplitStaff(t.StaffAssigned);
//                    for (int h = start; h < start + t.Duration; h++)
//                    {
//                        if (staffOcc.TryGetValue(staffCode, out var dayMap) && dayMap.TryGetValue(day, out var staffHours) && staffHours.Contains(h))
//                            return false;
//                        if (t.IsLab && !string.IsNullOrEmpty(t.LabId))
//                        {
//                            if (labOcc.TryGetValue(t.LabId, out var labDayMap) && labDayMap.TryGetValue(day, out var labHours) && labHours.Contains(h))
//                                return false;
//                        }
//                    }
//                    // Lab continuous block constraint example (4-hour slot must start at 1 or 4)
//                    if (t.IsLab && t.Duration == 4 && !(start == 1 || start == 4))
//                        return false;

//                    return true;
//                }

//                List<TaskUnit> CreateRandomIndividual()
//                {
//                    var individual = new List<TaskUnit>();
//                    foreach (var t in tasksToAssign)
//                    {
//                        var validSlots = new List<(string day, int start)>();
//                        foreach (var day in days)
//                        {
//                            for (int start = 1; start <= hours - t.Duration + 1; start++)
//                            {
//                                if (CanPlace(t, day, start))
//                                    validSlots.Add((day, start));
//                            }
//                        }
//                        if (validSlots.Count == 0)
//                            return null; // no valid slot

//                        var chosen = validSlots[rng.Next(validSlots.Count)];
//                        var copy = new TaskUnit
//                        {
//                            SubjectCode = t.SubjectCode,
//                            StaffAssigned = t.StaffAssigned,
//                            LabId = t.LabId,
//                            IsLab = t.IsLab,
//                            Duration = t.Duration,
//                            Day = chosen.day,
//                            StartHour = chosen.start,
//                            IsPlaced = true
//                        };
//                        individual.Add(copy);
//                    }
//                    return individual;
//                }

//                int Fitness(List<TaskUnit> individual)
//                {
//                    int penalty = 0;

//                    var staffSchedule = new Dictionary<string, Dictionary<string, HashSet<int>>>();
//                    var labSchedule = new Dictionary<string, Dictionary<string, HashSet<int>>>();

//                    foreach (var t in tasksToAssign)
//                    {
//                        var (_, staffCode) = SplitStaff(t.StaffAssigned);
//                        if (!staffSchedule.ContainsKey(staffCode))
//                            staffSchedule[staffCode] = days.ToDictionary(d => d, _ => new HashSet<int>());
//                        if (t.IsLab && !string.IsNullOrEmpty(t.LabId) && !labSchedule.ContainsKey(t.LabId))
//                            labSchedule[t.LabId] = days.ToDictionary(d => d, _ => new HashSet<int>());
//                    }

//                    foreach (var t in individual)
//                    {
//                        var (_, staffCode) = SplitStaff(t.StaffAssigned);
//                        for (int h = t.StartHour; h < t.StartHour + t.Duration; h++)
//                        {
//                            if (staffOcc.TryGetValue(staffCode, out var dayMap) && dayMap.TryGetValue(t.Day, out var existingHours) && existingHours.Contains(h))
//                                penalty += 10;
//                            if (t.IsLab && !string.IsNullOrEmpty(t.LabId) && labOcc.TryGetValue(t.LabId, out var labDayMap) && labDayMap.TryGetValue(t.Day, out var labExisting) && labExisting.Contains(h))
//                                penalty += 10;

//                            if (staffSchedule[staffCode][t.Day].Contains(h))
//                                penalty += 5;
//                            else
//                                staffSchedule[staffCode][t.Day].Add(h);

//                            if (t.IsLab && !string.IsNullOrEmpty(t.LabId))
//                            {
//                                if (labSchedule[t.LabId][t.Day].Contains(h))
//                                    penalty += 5;
//                                else
//                                    labSchedule[t.LabId][t.Day].Add(h);
//                            }
//                        }
//                        if (t.IsLab && t.Duration == 4 && !(t.StartHour == 1 || t.StartHour == 4))
//                            penalty += 20;
//                    }
//                    return penalty;
//                }

//                List<TaskUnit> TournamentSelection(List<List<TaskUnit>> population)
//                {
//                    int k = 3;
//                    var selected = new List<List<TaskUnit>>();
//                    for (int i = 0; i < k; i++)
//                        selected.Add(population[rng.Next(population.Count)]);
//                    return selected.OrderBy(ind => Fitness(ind)).First();
//                }

//                (List<TaskUnit>, List<TaskUnit>) Crossover(List<TaskUnit> parent1, List<TaskUnit> parent2)
//                {
//                    int point = rng.Next(1, parent1.Count);
//                    var child1 = new List<TaskUnit>();
//                    var child2 = new List<TaskUnit>();
//                    for (int i = 0; i < parent1.Count; i++)
//                    {
//                        child1.Add(i < point ? parent1[i] : parent2[i]);
//                        child2.Add(i < point ? parent2[i] : parent1[i]);
//                    }
//                    return (child1, child2);
//                }

//                void Mutate(List<TaskUnit> individual)
//                {
//                    for (int i = 0; i < individual.Count; i++)
//                    {
//                        if (rng.NextDouble() < mutationRate)
//                        {
//                            var t = tasksToAssign[i];
//                            var validSlots = new List<(string day, int start)>();
//                            foreach (var day in days)
//                            {
//                                for (int start = 1; start <= hours - t.Duration + 1; start++)
//                                {
//                                    if (CanPlace(t, day, start))
//                                        validSlots.Add((day, start));
//                                }
//                            }
//                            if (validSlots.Count > 0)
//                            {
//                                var chosen = validSlots[rng.Next(validSlots.Count)];
//                                individual[i].Day = chosen.day;
//                                individual[i].StartHour = chosen.start;
//                            }
//                        }
//                    }
//                }

//                var population = new List<List<TaskUnit>>();
//                for (int i = 0; i < populationSize; i++)
//                {
//                    var individual = CreateRandomIndividual();
//                    if (individual != null)
//                        population.Add(individual);
//                }
//                if (population.Count == 0)
//                    return (false, null);

//                for (int gen = 0; gen < maxGenerations; gen++)
//                {
//                    population = population.OrderBy(ind => Fitness(ind)).ToList();

//                    var best = population[0];
//                    if (Fitness(best) == 0)
//                    {
//                        return (true, best);
//                    }

//                    var nextGen = new List<List<TaskUnit>>();
//                    nextGen.Add(population[0]);
//                    nextGen.Add(population[1]);

//                    while (nextGen.Count < populationSize)
//                    {
//                        var parent1 = TournamentSelection(population);
//                        var parent2 = TournamentSelection(population);

//                        var (child1, child2) = Crossover(parent1, parent2);

//                        Mutate(child1);
//                        Mutate(child2);

//                        nextGen.Add(child1);
//                        if (nextGen.Count < populationSize)
//                            nextGen.Add(child2);
//                    }
//                    population = nextGen;
//                }

//                population = population.OrderBy(ind => Fitness(ind)).ToList();
//                if (Fitness(population[0]) == 0)
//                    return (true, population[0]);

//                return (false, null);
//            }

//            bool solved = await BacktrackWithGA(tasks);

//            if (!solved)
//                return Ok(new { message = "❌ Could not generate a conflict-free timetable.", receivedPayload = request });

//            // Save to DB atomically
//            await using var transaction = await conn.BeginTransactionAsync();

//            await using (var delClass = new NpgsqlCommand(@"
//            DELETE FROM classtimetable WHERE department_id=@department AND year=@year AND semester=@sem AND section=@section;", conn, transaction))
//            {
//                delClass.Parameters.AddWithValue("department", request.Department ?? "---");
//                delClass.Parameters.AddWithValue("year", request.Year ?? "---");
//                delClass.Parameters.AddWithValue("sem", request.Semester ?? "---");
//                delClass.Parameters.AddWithValue("section", request.Section ?? "---");
//                await delClass.ExecuteNonQueryAsync();
//            }

//            await using (var delLab = new NpgsqlCommand(@"
//            DELETE FROM labtimetable WHERE department=@department AND year=@year AND semester=@sem AND section=@section;", conn, transaction))
//            {
//                delLab.Parameters.AddWithValue("department", request.Department ?? "---");
//                delLab.Parameters.AddWithValue("year", request.Year ?? "---");
//                delLab.Parameters.AddWithValue("sem", request.Semester ?? "---");
//                delLab.Parameters.AddWithValue("section", request.Section ?? "---");
//                await delLab.ExecuteNonQueryAsync();
//            }

//            foreach (var t in tasks)
//            {
//                var (staffName, staffCode) = SplitStaff(t.StaffAssigned);
//                for (int h = t.StartHour; h < t.StartHour + t.Duration; h++)
//                {
//                    await using var icClass = new NpgsqlCommand(@"
//                    INSERT INTO classtimetable
//                    (staff_name, staff_code, department_id, year, semester, section, day, hour, subject_code, subject_name)
//                    VALUES
//                    (@staff_name, @staff_code, @department, @year, @semester, @section, @day, @hour, @subject_code, @subject_name);", conn, transaction);
//                    icClass.Parameters.AddWithValue("@staff_name", staffName ?? "---");
//                    icClass.Parameters.AddWithValue("@staff_code", staffCode ?? "---");
//                    icClass.Parameters.AddWithValue("@department", request.Department ?? "---");
//                    icClass.Parameters.AddWithValue("@year", request.Year ?? "---");
//                    icClass.Parameters.AddWithValue("@semester", request.Semester ?? "---");
//                    icClass.Parameters.AddWithValue("@section", request.Section ?? "---");
//                    icClass.Parameters.AddWithValue("@day", t.Day ?? "---");
//                    icClass.Parameters.AddWithValue("@hour", h);
//                    icClass.Parameters.AddWithValue("@subject_code", t.SubjectCode ?? "---");
//                    icClass.Parameters.AddWithValue("@subject_name", t.SubjectName ?? "---");
//                    await icClass.ExecuteNonQueryAsync();
//                }
//                if (t.IsLab && !string.IsNullOrEmpty(t.LabId))
//                {
//                    int labStart = t.StartHour == 4 ? 5 : t.StartHour;
//                    for (int h = labStart; h < t.StartHour + t.Duration; h++)
//                    {
//                        await using var icLab = new NpgsqlCommand(@"
//                        INSERT INTO labtimetable
//                        (lab_id, subject_code, subject_name, staff_name, department, year, semester, section, day, hour)
//                        VALUES
//                        (@lab_id, @subject_code, @subject_name, @staff_name, @department, @year, @semester, @section, @day, @hour);", conn, transaction);
//                        icLab.Parameters.AddWithValue("@lab_id", t.LabId ?? (object)DBNull.Value);
//                        icLab.Parameters.AddWithValue("@subject_code", t.SubjectCode ?? "---");
//                        icLab.Parameters.AddWithValue("@subject_name", t.SubjectName ?? "---");
//                        icLab.Parameters.AddWithValue("@staff_name", staffName ?? "---");
//                        icLab.Parameters.AddWithValue("@department", request.Department ?? "---");
//                        icLab.Parameters.AddWithValue("@year", request.Year ?? "---");
//                        icLab.Parameters.AddWithValue("@semester", request.Semester ?? "---");
//                        icLab.Parameters.AddWithValue("@section", request.Section ?? "---");
//                        icLab.Parameters.AddWithValue("@day", t.Day ?? "---");
//                        icLab.Parameters.AddWithValue("@hour", h);
//                        await icLab.ExecuteNonQueryAsync();
//                    }
//                }
//            }

//            await transaction.CommitAsync();

//            return Ok(new
//            {
//                message = "✅ Timetable generated successfully with advanced global conflict resolution.",
//                timetable = timetableGrid.Select(t => new { Day = t.Key, Slots = t.Value }),
//                usedLabIds = tasks.Where(t => t.IsLab && !string.IsNullOrEmpty(t.LabId)).Select(t => t.LabId).Distinct(),
//                receivedPayload = request
//            });
//        }
//    }
//}










//1 step away from perfection
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
//        private static readonly Random rng = new Random();

//        public TimetableController(IConfiguration configuration)
//        {
//            _configuration = configuration;
//        }

//        #region DTOs
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
//            public bool IsPlaced = false;
//            public List<(string day, int start)> Domain = new();
//        }
//        #endregion

//        #region Helper Methods
//        private void Shuffle<T>(IList<T> list)
//        {
//            int n = list.Count;
//            while (n > 1)
//            {
//                n--;
//                int k = rng.Next(n + 1);
//                (list[k], list[n]) = (list[n], list[k]);
//            }
//        }

//        private (string staffName, string staffCode) SplitStaff(string staffAssigned)
//        {
//            if (string.IsNullOrWhiteSpace(staffAssigned)) return ("---", "---");
//            var name = staffAssigned;
//            var code = staffAssigned;
//            if (staffAssigned.Contains("("))
//            {
//                var parts = staffAssigned.Split('(', StringSplitOptions.TrimEntries);
//                name = parts[0].Trim();
//                code = parts.Length > 1 ? parts[1].Replace(")", "").Trim() : "---";
//            }
//            return (name, code);
//        }

//        private bool IsFreeAndNoConflict(TaskUnit t, string day, int start,
//            Dictionary<string, Dictionary<string, HashSet<int>>> staffOcc,
//            Dictionary<string, Dictionary<string, HashSet<int>>> labOcc,
//            Dictionary<string, Dictionary<int, string>> timetableGrid,
//            int totalHours)
//        {
//            for (int h = start; h < start + t.Duration; h++)
//            {
//                if (h < 1 || h > totalHours) return false;
//                if (timetableGrid[day][h] != "---") return false;
//            }
//            var (_, staffCode) = SplitStaff(t.StaffAssigned);
//            for (int h = start; h < start + t.Duration; h++)
//            {
//                if (staffOcc.TryGetValue(staffCode, out var dm) && dm.TryGetValue(day, out var set) && set.Contains(h))
//                    return false;
//            }
//            if (t.IsLab && !string.IsNullOrEmpty(t.LabId))
//            {
//                for (int h = start; h < start + t.Duration; h++)
//                {
//                    if (labOcc.TryGetValue(t.LabId, out var dm) && dm.TryGetValue(day, out var set) && set.Contains(h))
//                        return false;
//                }
//                // Specific lab constraint: 4-hour labs start only at 1 or 4
//                if (t.Kind == "LAB4" && !(start == 1 || start == 4))
//                    return false;
//            }
//            return true;
//        }

//        private void AssignTask(TaskUnit t, (string day, int start) slot,
//            Dictionary<string, Dictionary<string, HashSet<int>>> staffOcc,
//            Dictionary<string, Dictionary<string, HashSet<int>>> labOcc,
//            Dictionary<string, Dictionary<int, string>> timetableGrid)
//        {
//            var (day, start) = slot;
//            var (_, staffCode) = SplitStaff(t.StaffAssigned);
//            for (int h = start; h < start + t.Duration; h++)
//            {
//                timetableGrid[day][h] = $"{t.SubjectCode} ({t.StaffAssigned})";
//                if (!staffOcc.ContainsKey(staffCode)) staffOcc[staffCode] = new Dictionary<string, HashSet<int>>();
//                if (!staffOcc[staffCode].ContainsKey(day)) staffOcc[staffCode][day] = new HashSet<int>();
//                staffOcc[staffCode][day].Add(h);
//                if (t.IsLab && !string.IsNullOrEmpty(t.LabId))
//                {
//                    if (!labOcc.ContainsKey(t.LabId)) labOcc[t.LabId] = new Dictionary<string, HashSet<int>>();
//                    if (!labOcc[t.LabId].ContainsKey(day)) labOcc[t.LabId][day] = new HashSet<int>();
//                    labOcc[t.LabId][day].Add(h);
//                }
//            }
//            t.Day = day;
//            t.StartHour = start;
//            t.IsPlaced = true;
//        }

//        private void UnassignTask(TaskUnit t,
//            Dictionary<string, Dictionary<string, HashSet<int>>> staffOcc,
//            Dictionary<string, Dictionary<string, HashSet<int>>> labOcc,
//            Dictionary<string, Dictionary<int, string>> timetableGrid)
//        {
//            if (!t.IsPlaced) return;
//            var day = t.Day;
//            var start = t.StartHour;
//            var (_, staffCode) = SplitStaff(t.StaffAssigned);
//            for (int h = start; h < start + t.Duration; h++)
//            {
//                timetableGrid[day][h] = "---";
//                staffOcc[staffCode][day].Remove(h);
//                if (t.IsLab && !string.IsNullOrEmpty(t.LabId))
//                    labOcc[t.LabId][day].Remove(h);
//            }
//            t.IsPlaced = false;
//            t.Day = null;
//            t.StartHour = 0;
//        }

//        private bool PropagateConstraints(List<TaskUnit> tasksList, TaskUnit assignedTask, (string day, int start) assignedSlot)
//        {
//            // Stronger domain pruning with dependency checks
//            var snapshotDomains = tasksList.ToDictionary(t => t, t => t.Domain.ToList());
//            assignedTask.Domain = new List<(string, int)> { assignedSlot };

//            foreach (var other in tasksList)
//            {
//                if (other == assignedTask || other.IsPlaced) continue;
//                var filtered = new List<(string day, int start)>();
//                foreach (var pos in other.Domain)
//                {
//                    bool conflict = false;
//                    if (pos.day == assignedSlot.day)
//                    {
//                        int start1 = pos.start;
//                        int end1 = pos.start + other.Duration - 1;
//                        int start2 = assignedSlot.start;
//                        int end2 = assignedSlot.start + assignedTask.Duration - 1;
//                        bool overlap = end1 >= start2 && end2 >= start1;
//                        if (overlap)
//                        {
//                            var (_, staff1) = SplitStaff(other.StaffAssigned);
//                            var (_, staff2) = SplitStaff(assignedTask.StaffAssigned);
//                            if (staff1 == staff2) conflict = true;

//                            if (!conflict && other.IsLab && assignedTask.IsLab &&
//                                !string.IsNullOrEmpty(other.LabId) && !string.IsNullOrEmpty(assignedTask.LabId) && other.LabId == assignedTask.LabId)
//                                conflict = true;
//                        }
//                    }
//                    if (!conflict) filtered.Add(pos);
//                }
//                other.Domain = filtered;
//                if (other.Domain.Count == 0)
//                {
//                    foreach (var kvp in snapshotDomains) kvp.Key.Domain = kvp.Value;
//                    return false;
//                }
//            }

//            return true;
//        }

//        private async Task<List<TaskUnit>> CollectRelatedTasksAsync(
//            TaskUnit task,
//            NpgsqlConnection conn,
//            TimetableRequest request,
//            Dictionary<string, Dictionary<string, HashSet<int>>> staffOcc,
//            Dictionary<string, Dictionary<string, HashSet<int>>> labOcc)
//        {
//            var relatedTasks = new List<TaskUnit>();
//            var involvedStaffIds = new HashSet<string>();
//            var involvedLabIds = new HashSet<string>();

//            var (_, staffCode) = SplitStaff(task.StaffAssigned);
//            involvedStaffIds.Add(staffCode);
//            if (!string.IsNullOrEmpty(task.LabId)) involvedLabIds.Add(task.LabId);

//            string classSql = @"
//                SELECT staff_code, subject_code, subject_name, day, hour
//                FROM classtimetable
//                WHERE staff_code = ANY(@staffIds)
//                  AND year = @year AND semester = @sem AND department_id = @dept AND section = @section
//                ORDER BY day, hour";

//            string labSql = @"
//                SELECT staff_code, subject_code, subject_name, day, hour, lab_id
//                FROM labtimetable
//                WHERE (lab_id = ANY(@labIds) OR staff_code = ANY(@staffIds))
//                  AND year = @year AND semester = @sem AND department = @dept AND section = @section
//                ORDER BY day, hour";

//            var entries = new List<(string staff_code, string subject_code, string subject_name, string day, int hour, string lab_id)>();

//            using (var cmd = new NpgsqlCommand(classSql, conn))
//            {
//                cmd.Parameters.AddWithValue("staffIds", involvedStaffIds.ToArray());
//                cmd.Parameters.AddWithValue("year", request.Year);
//                cmd.Parameters.AddWithValue("sem", request.Semester);
//                cmd.Parameters.AddWithValue("dept", request.Department);
//                cmd.Parameters.AddWithValue("section", request.Section);

//                using var reader = await cmd.ExecuteReaderAsync();
//                while (await reader.ReadAsync())
//                {
//                    entries.Add((
//                        reader.GetString(0),
//                        reader.GetString(1),
//                        reader.GetString(2),
//                        reader.GetString(3),
//                        reader.GetInt32(4),
//                        null
//                    ));
//                }
//                reader.Close();
//            }

//            using (var cmd = new NpgsqlCommand(labSql, conn))
//            {
//                cmd.Parameters.AddWithValue("labIds", involvedLabIds.Count > 0 ? involvedLabIds.ToArray() : new string[] { "" });
//                cmd.Parameters.AddWithValue("staffIds", involvedStaffIds.ToArray());
//                cmd.Parameters.AddWithValue("year", request.Year);
//                cmd.Parameters.AddWithValue("sem", request.Semester);
//                cmd.Parameters.AddWithValue("dept", request.Department);
//                cmd.Parameters.AddWithValue("section", request.Section);

//                using var reader = await cmd.ExecuteReaderAsync();
//                while (await reader.ReadAsync())
//                {
//                    entries.Add((
//                        reader.GetString(0),
//                        reader.GetString(1),
//                        reader.GetString(2),
//                        reader.GetString(3),
//                        reader.GetInt32(4),
//                        reader.IsDBNull(5) ? null : reader.GetString(5)
//                    ));
//                }
//                reader.Close();
//            }

//            var grouped = entries.GroupBy(e => (e.subject_code, e.staff_code, e.lab_id, e.day));
//            foreach (var group in grouped)
//            {
//                var hours = group.Select(e => e.hour).OrderBy(h => h).ToList();
//                int start = hours.First();
//                int duration = hours.Count;
//                relatedTasks.Add(new TaskUnit
//                {
//                    SubjectCode = group.Key.subject_code,
//                    StaffAssigned = group.Key.staff_code,
//                    SubjectName = group.First().subject_name,
//                    LabId = group.Key.lab_id,
//                    IsLab = !string.IsNullOrEmpty(group.Key.lab_id),
//                    Duration = duration,
//                    Kind = !string.IsNullOrEmpty(group.Key.lab_id) ? "LAB" : "TH",
//                    Day = group.Key.day,
//                    StartHour = start,
//                    IsPlaced = true
//                });
//            }

//            return relatedTasks;
//        }
//        #endregion

//        #region GeneticAlgorithmRescheduling

//        private async Task<(bool Succeeded, List<TaskUnit> Result)> RunGeneticAlgorithmAsync(
//            List<TaskUnit> tasksToAssign,
//            string[] days,
//            int hours,
//            Dictionary<string, Dictionary<string, HashSet<int>>> staffOcc,
//            Dictionary<string, Dictionary<string, HashSet<int>>> labOcc)
//        {
//            const int populationSize = 50;
//            const int maxGenerations = 150;
//            const double mutationRate = 0.15;

//            bool CanPlace(TaskUnit t, string day, int start)
//            {
//                if (start < 1 || start + t.Duration - 1 > hours)
//                    return false;

//                var (_, staffCode) = SplitStaff(t.StaffAssigned);
//                for (int h = start; h < start + t.Duration; h++)
//                {
//                    if (staffOcc.TryGetValue(staffCode, out var dayMap) && dayMap.TryGetValue(day, out var staffHours) && staffHours.Contains(h))
//                        return false;
//                    if (t.IsLab && !string.IsNullOrEmpty(t.LabId))
//                    {
//                        if (labOcc.TryGetValue(t.LabId, out var labDayMap) && labDayMap.TryGetValue(day, out var labHours) && labHours.Contains(h))
//                            return false;
//                    }
//                }
//                if (t.IsLab && t.Duration == 4 && !(start == 1 || start == 4))
//                    return false;

//                return true;
//            }

//            List<TaskUnit> CreateRandomIndividual()
//            {
//                var individual = new List<TaskUnit>();
//                foreach (var t in tasksToAssign)
//                {
//                    var validSlots = new List<(string day, int start)>();
//                    foreach (var day in days)
//                    {
//                        for (int start = 1; start <= hours - t.Duration + 1; start++)
//                        {
//                            if (CanPlace(t, day, start))
//                                validSlots.Add((day, start));
//                        }
//                    }
//                    if (validSlots.Count == 0)
//                        return null; // no valid slot
//                    var chosen = validSlots[rng.Next(validSlots.Count)];
//                    var copy = new TaskUnit
//                    {
//                        SubjectCode = t.SubjectCode,
//                        StaffAssigned = t.StaffAssigned,
//                        LabId = t.LabId,
//                        IsLab = t.IsLab,
//                        Duration = t.Duration,
//                        Day = chosen.day,
//                        StartHour = chosen.start,
//                        IsPlaced = true
//                    };
//                    individual.Add(copy);
//                }
//                return individual;
//            }

//            int Fitness(List<TaskUnit> individual)
//            {
//                int penalty = 0;
//                var staffSchedule = new Dictionary<string, Dictionary<string, HashSet<int>>>();
//                var labSchedule = new Dictionary<string, Dictionary<string, HashSet<int>>>();

//                foreach (var t in tasksToAssign)
//                {
//                    var (_, staffCode) = SplitStaff(t.StaffAssigned);
//                    if (!staffSchedule.ContainsKey(staffCode))
//                        staffSchedule[staffCode] = days.ToDictionary(d => d, _ => new HashSet<int>());
//                    if (t.IsLab && !string.IsNullOrEmpty(t.LabId) && !labSchedule.ContainsKey(t.LabId))
//                        labSchedule[t.LabId] = days.ToDictionary(d => d, _ => new HashSet<int>());
//                }

//                foreach (var t in individual)
//                {
//                    var (_, staffCode) = SplitStaff(t.StaffAssigned);
//                    for (int h = t.StartHour; h < t.StartHour + t.Duration; h++)
//                    {
//                        if (staffOcc.TryGetValue(staffCode, out var dayMap) && dayMap.TryGetValue(t.Day, out var existingHours) && existingHours.Contains(h))
//                            penalty += 10;

//                        if (t.IsLab && !string.IsNullOrEmpty(t.LabId) && labOcc.TryGetValue(t.LabId, out var labDayMap) && labDayMap.TryGetValue(t.Day, out var labExisting) && labExisting.Contains(h))
//                            penalty += 10;

//                        if (staffSchedule[staffCode][t.Day].Contains(h))
//                            penalty += 5;
//                        else
//                            staffSchedule[staffCode][t.Day].Add(h);

//                        if (t.IsLab && !string.IsNullOrEmpty(t.LabId))
//                        {
//                            if (labSchedule[t.LabId][t.Day].Contains(h))
//                                penalty += 5;
//                            else
//                                labSchedule[t.LabId][t.Day].Add(h);
//                        }
//                    }

//                    if (t.IsLab && t.Duration == 4 && !(t.StartHour == 1 || t.StartHour == 4))
//                        penalty += 20;
//                }

//                return penalty;
//            }

//            List<TaskUnit> TournamentSelection(List<List<TaskUnit>> population)
//            {
//                int k = 3;
//                var selected = new List<List<TaskUnit>>();
//                for (int i = 0; i < k; i++)
//                    selected.Add(population[rng.Next(population.Count)]);
//                return selected.OrderBy(ind => Fitness(ind)).First();
//            }

//            (List<TaskUnit>, List<TaskUnit>) Crossover(List<TaskUnit> parent1, List<TaskUnit> parent2)
//            {
//                int point = rng.Next(1, parent1.Count);
//                var child1 = new List<TaskUnit>();
//                var child2 = new List<TaskUnit>();
//                for (int i = 0; i < parent1.Count; i++)
//                {
//                    child1.Add(i < point ? parent1[i] : parent2[i]);
//                    child2.Add(i < point ? parent2[i] : parent1[i]);
//                }
//                return (child1, child2);
//            }

//            void Mutate(List<TaskUnit> individual)
//            {
//                for (int i = 0; i < individual.Count; i++)
//                {
//                    if (rng.NextDouble() < mutationRate)
//                    {
//                        var t = tasksToAssign[i];
//                        var validSlots = new List<(string day, int start)>();
//                        foreach (var day in days)
//                        {
//                            for (int start = 1; start <= hours - t.Duration + 1; start++)
//                            {
//                                if (CanPlace(t, day, start))
//                                    validSlots.Add((day, start));
//                            }
//                        }
//                        if (validSlots.Count > 0)
//                        {
//                            var chosen = validSlots[rng.Next(validSlots.Count)];
//                            individual[i].Day = chosen.day;
//                            individual[i].StartHour = chosen.start;
//                        }
//                    }
//                }
//            }

//            var population = new List<List<TaskUnit>>();
//            for (int i = 0; i < populationSize; i++)
//            {
//                var individual = CreateRandomIndividual();
//                if (individual != null)
//                    population.Add(individual);
//            }
//            if (population.Count == 0)
//                return (false, null);

//            for (int gen = 0; gen < maxGenerations; gen++)
//            {
//                population = population.OrderBy(ind => Fitness(ind)).ToList();
//                var best = population[0];
//                if (Fitness(best) == 0)
//                {
//                    return (true, best);
//                }
//                var nextGen = new List<List<TaskUnit>>
//                {
//                    population[0], population[1]
//                };
//                while (nextGen.Count < populationSize)
//                {
//                    var parent1 = TournamentSelection(population);
//                    var parent2 = TournamentSelection(population);
//                    var (child1, child2) = Crossover(parent1, parent2);
//                    Mutate(child1);
//                    Mutate(child2);
//                    nextGen.Add(child1);
//                    if (nextGen.Count < populationSize)
//                        nextGen.Add(child2);
//                }
//                population = nextGen;
//            }
//            population = population.OrderBy(ind => Fitness(ind)).ToList();
//            if (Fitness(population[0]) == 0)
//                return (true, population[0]);
//            return (false, null);
//        }
//        #endregion

//        [HttpPost("generateCrossDepartmentTimetableBacktracking")]
//        public async Task<IActionResult> GenerateTimetableWithGARescheduling([FromBody] TimetableRequest request)
//        {
//            var cs = _configuration.GetConnectionString("DefaultConnection");
//            await using var conn = new NpgsqlConnection(cs);
//            await conn.OpenAsync();

//            string[] DAYS = new[] { "Mon", "Tue", "Wed", "Thu", "Fri" };
//            int HOURS = Math.Max(1, request.TotalHoursPerDay);

//            // Initialize timetable grid with empty slots
//            var timetableGrid = DAYS.ToDictionary(d => d, d => Enumerable.Range(1, HOURS).ToDictionary(h => h, _ => "---"));

//            var staffOcc = new Dictionary<string, Dictionary<string, HashSet<int>>>();
//            var labOcc = new Dictionary<string, Dictionary<string, HashSet<int>>>();

//            void EnsureDayMap<TKey>(Dictionary<TKey, Dictionary<string, HashSet<int>>> map, TKey key)
//            {
//                if (!map.ContainsKey(key))
//                    map[key] = DAYS.ToDictionary(d => d, _ => new HashSet<int>());
//            }

//            // Load existing staff occupancy from DB
//            await using (var cmd = new NpgsqlCommand("SELECT staff_code, day, hour FROM classtimetable", conn))
//            await using (var rd = await cmd.ExecuteReaderAsync())
//            {
//                while (await rd.ReadAsync())
//                {
//                    var sc = rd["staff_code"] as string ?? "---";
//                    var day = rd["day"] as string ?? "Mon";
//                    var hr = (int)rd["hour"];
//                    EnsureDayMap(staffOcc, sc);
//                    staffOcc[sc][day].Add(hr);
//                }
//            }

//            // Load existing lab occupancy from DB
//            await using (var cmd = new NpgsqlCommand("SELECT lab_id, day, hour FROM labtimetable", conn))
//            await using (var rd = await cmd.ExecuteReaderAsync())
//            {
//                while (await rd.ReadAsync())
//                {
//                    var lab = rd["lab_id"] as string ?? "---";
//                    var day = rd["day"] as string ?? "Mon";
//                    var hr = (int)rd["hour"];
//                    EnsureDayMap(labOcc, lab);
//                    labOcc[lab][day].Add(hr);
//                }
//            }

//            var subjects = request.Subjects?.Where(s => !string.IsNullOrWhiteSpace(s.StaffAssigned)).ToList();
//            if (subjects == null || subjects.Count == 0)
//                return BadRequest(new { message = "❌ No valid subjects with assigned staff found." });

//            // Create Tasks for scheduling
//            var tasks = new List<TaskUnit>();
//            foreach (var s in subjects)
//            {
//                var type = (s.SubjectType ?? "theory").Trim().ToLower();
//                switch (type)
//                {
//                    case "lab":
//                        tasks.Add(new TaskUnit
//                        {
//                            SubjectCode = s.SubjectCode ?? "---",
//                            SubjectName = s.SubjectName ?? "---",
//                            StaffAssigned = s.StaffAssigned,
//                            LabId = s.LabId,
//                            IsLab = true,
//                            Duration = 4,
//                            Kind = "LAB4"
//                        });
//                        break;
//                    case "embedded":
//                        tasks.Add(new TaskUnit
//                        {
//                            SubjectCode = s.SubjectCode ?? "---",
//                            SubjectName = s.SubjectName ?? "---",
//                            StaffAssigned = s.StaffAssigned,
//                            LabId = s.LabId,
//                            IsLab = true,
//                            Duration = 2,
//                            Kind = "EMB_LAB2"
//                        });
//                        tasks.Add(new TaskUnit
//                        {
//                            SubjectCode = s.SubjectCode ?? "---",
//                            SubjectName = s.SubjectName ?? "---",
//                            StaffAssigned = s.StaffAssigned,
//                            IsLab = false,
//                            Duration = 1,
//                            Kind = "EMB_TH1"
//                        });
//                        tasks.Add(new TaskUnit
//                        {
//                            SubjectCode = s.SubjectCode ?? "---",
//                            SubjectName = s.SubjectName ?? "---",
//                            StaffAssigned = s.StaffAssigned,
//                            IsLab = false,
//                            Duration = 1,
//                            Kind = "EMB_TH1"
//                        });
//                        break;
//                    default:
//                        int count = Math.Max(0, s.Credit);
//                        for (int i = 0; i < count; i++)
//                        {
//                            tasks.Add(new TaskUnit
//                            {
//                                SubjectCode = s.SubjectCode ?? "---",
//                                SubjectName = s.SubjectName ?? "---",
//                                StaffAssigned = s.StaffAssigned,
//                                IsLab = false,
//                                Duration = 1,
//                                Kind = "TH1"
//                            });
//                        }
//                        break;
//                }
//            }

//            Shuffle(tasks);

//            // Initialize each task's domain with all feasible slots
//            foreach (var t in tasks)
//            {
//                t.Domain.Clear();
//                foreach (var day in DAYS)
//                {
//                    for (int start = 1; start <= HOURS - t.Duration + 1; start++)
//                    {
//                        if (IsFreeAndNoConflict(t, day, start, staffOcc, labOcc, timetableGrid, HOURS))
//                        {
//                            t.Domain.Add((day, start));
//                        }
//                    }
//                }
//                if (t.Domain.Count == 0)
//                    return Ok(new { message = $"No initial available slot for task {t.SubjectCode}.", receivedPayload = request });
//                Shuffle(t.Domain);
//            }

//            // Main backtracking with GA fallback
//            async Task<bool> BacktrackWithGA(List<TaskUnit> taskList)
//            {
//                if (taskList.All(t => t.IsPlaced)) return true;

//                var currentTask = taskList.Where(t => !t.IsPlaced).OrderBy(t => t.Domain.Count).FirstOrDefault();
//                if (currentTask == null) return true;

//                Shuffle(currentTask.Domain);

//                foreach (var slot in currentTask.Domain)
//                {
//                    if (IsFreeAndNoConflict(currentTask, slot.day, slot.start, staffOcc, labOcc, timetableGrid, HOURS))
//                    {
//                        AssignTask(currentTask, slot, staffOcc, labOcc, timetableGrid);
//                        if (PropagateConstraints(taskList, currentTask, slot))
//                        {
//                            if (await BacktrackWithGA(taskList)) return true;
//                        }

//                        // On conflict, rollback related tasks and use GA
//                        var conflictTasks = await CollectRelatedTasksAsync(currentTask, conn, request, staffOcc, labOcc);
//                        var reassignmentTasks = new List<TaskUnit> { currentTask };
//                        foreach (var t in conflictTasks)
//                            if (!reassignmentTasks.Contains(t)) reassignmentTasks.Add(t);

//                        foreach (var t in reassignmentTasks)
//                            UnassignTask(t, staffOcc, labOcc, timetableGrid);

//                        var gaResult = await RunGeneticAlgorithmAsync(reassignmentTasks, DAYS, HOURS, staffOcc, labOcc);

//                        if (gaResult.Succeeded)
//                        {
//                            foreach (var t in reassignmentTasks)
//                            {
//                                t.Domain.Clear();
//                                t.Domain.Add((t.Day, t.StartHour));
//                                AssignTask(t, (t.Day, t.StartHour), staffOcc, labOcc, timetableGrid);
//                            }

//                            if (await BacktrackWithGA(taskList))
//                                return true;
//                        }
//                        else
//                        {
//                            foreach (var t in reassignmentTasks)
//                                if (t.Day != null && !t.IsPlaced)
//                                    AssignTask(t, (t.Day, t.StartHour), staffOcc, labOcc, timetableGrid);
//                        }
//                        UnassignTask(currentTask, staffOcc, labOcc, timetableGrid);
//                    }
//                }
//                return false;
//            }

//            bool solved = await BacktrackWithGA(tasks);

//            if (!solved)
//                return Ok(new { message = "❌ Could not generate a conflict-free timetable.", receivedPayload = request });

//            // Validate final timetable here for any runtime conflicts before commit (Extra safety)
//            if (!ValidateFinalTimetable(tasks, HOURS))
//            {
//                return Ok(new { message = "❌ Timetable validation failed after generation.", receivedPayload = request });
//            }

//            // Atomic DB save transaction
//            await using var transaction = await conn.BeginTransactionAsync();

//            // Clear old timetable entries
//            await using (var delClass = new NpgsqlCommand(@"
//                DELETE FROM classtimetable WHERE department_id=@department AND year=@year AND semester=@sem AND section=@section;", conn, transaction))
//            {
//                delClass.Parameters.AddWithValue("department", request.Department ?? "---");
//                delClass.Parameters.AddWithValue("year", request.Year ?? "---");
//                delClass.Parameters.AddWithValue("sem", request.Semester ?? "---");
//                delClass.Parameters.AddWithValue("section", request.Section ?? "---");
//                await delClass.ExecuteNonQueryAsync();
//            }

//            await using (var delLab = new NpgsqlCommand(@"
//                DELETE FROM labtimetable WHERE department=@department AND year=@year AND semester=@sem AND section=@section;", conn, transaction))
//            {
//                delLab.Parameters.AddWithValue("department", request.Department ?? "---");
//                delLab.Parameters.AddWithValue("year", request.Year ?? "---");
//                delLab.Parameters.AddWithValue("sem", request.Semester ?? "---");
//                delLab.Parameters.AddWithValue("section", request.Section ?? "---");
//                await delLab.ExecuteNonQueryAsync();
//            }

//            // Insert new timetable data
//            foreach (var t in tasks)
//            {
//                var (staffName, staffCode) = SplitStaff(t.StaffAssigned);
//                for (int h = t.StartHour; h < t.StartHour + t.Duration; h++)
//                {
//                    await using var icClass = new NpgsqlCommand(@"
//                        INSERT INTO classtimetable
//                        (staff_name, staff_code, department_id, year, semester, section, day, hour, subject_code, subject_name)
//                        VALUES
//                        (@staff_name, @staff_code, @department, @year, @semester, @section, @day, @hour, @subject_code, @subject_name);", conn, transaction);
//                    icClass.Parameters.AddWithValue("@staff_name", staffName ?? "---");
//                    icClass.Parameters.AddWithValue("@staff_code", staffCode ?? "---");
//                    icClass.Parameters.AddWithValue("@department", request.Department ?? "---");
//                    icClass.Parameters.AddWithValue("@year", request.Year ?? "---");
//                    icClass.Parameters.AddWithValue("@semester", request.Semester ?? "---");
//                    icClass.Parameters.AddWithValue("@section", request.Section ?? "---");
//                    icClass.Parameters.AddWithValue("@day", t.Day ?? "---");
//                    icClass.Parameters.AddWithValue("@hour", h);
//                    icClass.Parameters.AddWithValue("@subject_code", t.SubjectCode ?? "---");
//                    icClass.Parameters.AddWithValue("@subject_name", t.SubjectName ?? "---");
//                    await icClass.ExecuteNonQueryAsync();
//                }

//                if (t.IsLab && !string.IsNullOrEmpty(t.LabId))
//                {
//                    int labStart = t.StartHour == 4 ? 5 : t.StartHour;
//                    for (int h = labStart; h < t.StartHour + t.Duration; h++)
//                    {
//                        await using var icLab = new NpgsqlCommand(@"
//                            INSERT INTO labtimetable
//                            (lab_id, subject_code, subject_name, staff_name, department, year, semester, section, day, hour)
//                            VALUES
//                            (@lab_id, @subject_code, @subject_name, @staff_name, @department, @year, @semester, @section, @day, @hour);", conn, transaction);
//                        icLab.Parameters.AddWithValue("@lab_id", t.LabId ?? (object)DBNull.Value);
//                        icLab.Parameters.AddWithValue("@subject_code", t.SubjectCode ?? "---");
//                        icLab.Parameters.AddWithValue("@subject_name", t.SubjectName ?? "---");
//                        icLab.Parameters.AddWithValue("@staff_name", staffName ?? "---");
//                        icLab.Parameters.AddWithValue("@department", request.Department ?? "---");
//                        icLab.Parameters.AddWithValue("@year", request.Year ?? "---");
//                        icLab.Parameters.AddWithValue("@semester", request.Semester ?? "---");
//                        icLab.Parameters.AddWithValue("@section", request.Section ?? "---");
//                        icLab.Parameters.AddWithValue("@day", t.Day ?? "---");
//                        icLab.Parameters.AddWithValue("@hour", h);
//                        await icLab.ExecuteNonQueryAsync();
//                    }
//                }
//            }

//            await transaction.CommitAsync();

//            return Ok(new
//            {
//                message = "✅ Timetable generated successfully with enhanced global conflict resolution.",
//                timetable = timetableGrid.Select(t => new { Day = t.Key, Slots = t.Value }),
//                usedLabIds = tasks.Where(t => t.IsLab && !string.IsNullOrEmpty(t.LabId)).Select(t => t.LabId).Distinct(),
//                receivedPayload = request
//            });
//        }

//        private bool ValidateFinalTimetable(List<TaskUnit> tasks, int totalHours)
//        {
//            var staffSchedule = new Dictionary<string, Dictionary<string, HashSet<int>>>();
//            var labSchedule = new Dictionary<string, Dictionary<string, HashSet<int>>>();

//            foreach (var t in tasks)
//            {
//                var (_, staffCode) = SplitStaff(t.StaffAssigned);
//                if (!staffSchedule.ContainsKey(staffCode))
//                    staffSchedule[staffCode] = new Dictionary<string, HashSet<int>>();
//                if (!staffSchedule[staffCode].ContainsKey(t.Day))
//                    staffSchedule[staffCode][t.Day] = new HashSet<int>();

//                if (t.IsLab && !string.IsNullOrEmpty(t.LabId))
//                {
//                    if (!labSchedule.ContainsKey(t.LabId))
//                        labSchedule[t.LabId] = new Dictionary<string, HashSet<int>>();
//                    if (!labSchedule[t.LabId].ContainsKey(t.Day))
//                        labSchedule[t.LabId][t.Day] = new HashSet<int>();
//                }

//                for (int h = t.StartHour; h < t.StartHour + t.Duration; h++)
//                {
//                    if (h < 1 || h > totalHours)
//                        return false;

//                    if (staffSchedule[staffCode][t.Day].Contains(h))
//                        return false;
//                    else
//                        staffSchedule[staffCode][t.Day].Add(h);

//                    if (t.IsLab && !string.IsNullOrEmpty(t.LabId))
//                    {
//                        if (labSchedule[t.LabId][t.Day].Contains(h))
//                            return false;
//                        else
//                            labSchedule[t.LabId][t.Day].Add(h);
//                    }

//                    // Lab block constraints
//                    if (t.IsLab && t.Duration == 4 && !(t.StartHour == 1 || t.StartHour == 4))
//                        return false;
//                }
//            }
//            return true;
//        }
//    }
//}











//flop google ortools
//using Google.OrTools.Sat;
//using Microsoft.AspNetCore.Mvc;
//using Microsoft.Extensions.Configuration;
//using Npgsql;
//using System;
//using System.Collections.Generic;
//using System.Linq;
//using System.Threading.Tasks;
//using static System.Runtime.InteropServices.JavaScript.JSType;

//namespace Timetablegenerator.Controllers
//{
//    [ApiController]
//    [Route("api/[controller]")]
//    public class TimetableController : ControllerBase
//    {
//        private readonly IConfiguration _configuration;
//        private static readonly Random rng = new Random();

//        public TimetableController(IConfiguration configuration)
//        {
//            _configuration = configuration;
//        }

//        #region DTOs
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
//            public bool IsPlaced = false;
//            public List<(string day, int start)> Domain = new();
//        }
//        #endregion

//        #region Helper Methods

//        private void Shuffle<T>(IList<T> list)
//        {
//            int n = list.Count;
//            while (n > 1)
//            {
//                n--;
//                int k = rng.Next(n + 1);
//                (list[k], list[n]) = (list[n], list[k]);
//            }
//        }

//        private (string staffName, string staffCode) SplitStaff(string staffAssigned)
//        {
//            if (string.IsNullOrWhiteSpace(staffAssigned)) return ("---", "---");
//            var name = staffAssigned;
//            var code = staffAssigned;
//            if (staffAssigned.Contains("("))
//            {
//                var parts = staffAssigned.Split('(', StringSplitOptions.TrimEntries);
//                name = parts[0].Trim();
//                code = parts.Length > 1 ? parts[1].Replace(")", "").Trim() : "---";
//            }
//            return (name, code);
//        }

//        private bool IsFreeAndNoConflict(TaskUnit t, string day, int start,
//            Dictionary<string, Dictionary<string, HashSet<int>>> staffOcc,
//            Dictionary<string, Dictionary<string, HashSet<int>>> labOcc,
//            Dictionary<string, Dictionary<int, string>> timetableGrid,
//            int totalHours)
//        {
//            for (int h = start; h < start + t.Duration; h++)
//            {
//                if (h < 1 || h > totalHours) return false;
//                if (timetableGrid[day][h] != "---") return false;
//            }
//            var (_, staffCode) = SplitStaff(t.StaffAssigned);
//            for (int h = start; h < start + t.Duration; h++)
//            {
//                if (staffOcc.TryGetValue(staffCode, out var dm) && dm.TryGetValue(day, out var set) && set.Contains(h))
//                    return false;
//            }
//            if (t.IsLab && !string.IsNullOrEmpty(t.LabId))
//            {
//                for (int h = start; h < start + t.Duration; h++)
//                {
//                    if (labOcc.TryGetValue(t.LabId, out var dm) && dm.TryGetValue(day, out var set) && set.Contains(h))
//                        return false;
//                }
//                if (t.Kind == "LAB4" && !(start == 1 || start == 4))
//                    return false;
//            }
//            return true;
//        }

//        private void AssignTask(TaskUnit t, (string day, int start) slot,
//            Dictionary<string, Dictionary<string, HashSet<int>>> staffOcc,
//            Dictionary<string, Dictionary<string, HashSet<int>>> labOcc,
//            Dictionary<string, Dictionary<int, string>> timetableGrid)
//        {
//            var (day, start) = slot;
//            var (_, staffCode) = SplitStaff(t.StaffAssigned);
//            for (int h = start; h < start + t.Duration; h++)
//            {
//                timetableGrid[day][h] = $"{t.SubjectCode} ({t.StaffAssigned})";
//                if (!staffOcc.ContainsKey(staffCode)) staffOcc[staffCode] = new Dictionary<string, HashSet<int>>();
//                if (!staffOcc[staffCode].ContainsKey(day)) staffOcc[staffCode][day] = new HashSet<int>();
//                staffOcc[staffCode][day].Add(h);
//                if (t.IsLab && !string.IsNullOrEmpty(t.LabId))
//                {
//                    if (!labOcc.ContainsKey(t.LabId)) labOcc[t.LabId] = new Dictionary<string, HashSet<int>>();
//                    if (!labOcc[t.LabId].ContainsKey(day)) labOcc[t.LabId][day] = new HashSet<int>();
//                    labOcc[t.LabId][day].Add(h);
//                }
//            }
//            t.Day = day;
//            t.StartHour = start;
//            t.IsPlaced = true;
//        }

//        private void UnassignTask(TaskUnit t,
//            Dictionary<string, Dictionary<string, HashSet<int>>> staffOcc,
//            Dictionary<string, Dictionary<string, HashSet<int>>> labOcc,
//            Dictionary<string, Dictionary<int, string>> timetableGrid)
//        {
//            if (!t.IsPlaced) return;
//            var day = t.Day;
//            var start = t.StartHour;
//            var (_, staffCode) = SplitStaff(t.StaffAssigned);
//            for (int h = start; h < start + t.Duration; h++)
//            {
//                timetableGrid[day][h] = "---";
//                staffOcc[staffCode][day].Remove(h);
//                if (t.IsLab && !string.IsNullOrEmpty(t.LabId))
//                    labOcc[t.LabId][day].Remove(h);
//            }
//            t.IsPlaced = false;
//            t.Day = null;
//            t.StartHour = 0;
//        }

//        private bool PropagateConstraints(List<TaskUnit> tasksList, TaskUnit assignedTask, (string day, int start) assignedSlot)
//        {
//            var snapshotDomains = tasksList.ToDictionary(t => t, t => t.Domain.ToList());
//            assignedTask.Domain = new List<(string, int)> { assignedSlot };
//            foreach (var other in tasksList)
//            {
//                if (other == assignedTask || other.IsPlaced) continue;
//                var filtered = new List<(string day, int start)>();
//                foreach (var pos in other.Domain)
//                {
//                    bool conflict = false;
//                    if (pos.day == assignedSlot.day)
//                    {
//                        int start1 = pos.start;
//                        int end1 = pos.start + other.Duration - 1;
//                        int start2 = assignedSlot.start;
//                        int end2 = assignedSlot.start + assignedTask.Duration - 1;
//                        bool overlap = end1 >= start2 && end2 >= start1;
//                        if (overlap)
//                        {
//                            var (_, staff1) = SplitStaff(other.StaffAssigned);
//                            var (_, staff2) = SplitStaff(assignedTask.StaffAssigned);
//                            if (staff1 == staff2) conflict = true;
//                            if (!conflict && other.IsLab && assignedTask.IsLab &&
//                                !string.IsNullOrEmpty(other.LabId) && !string.IsNullOrEmpty(assignedTask.LabId) && other.LabId == assignedTask.LabId)
//                                conflict = true;
//                        }
//                    }
//                    if (!conflict) filtered.Add(pos);
//                }
//                other.Domain = filtered;
//                if (other.Domain.Count == 0)
//                {
//                    foreach (var kvp in snapshotDomains) kvp.Key.Domain = kvp.Value;
//                    return false;
//                }
//            }
//            return true;
//        }

//        // Transitive closure of related tasks (staff/lab conflicts)
//        private async Task<List<TaskUnit>> CollectTransitiveRelatedTasksAsync(
//       TaskUnit task,
//       NpgsqlConnection conn,
//       TimetableRequest request)
//        {
//            var queue = new Queue<string>(); // For staff and lab IDs to explore
//            var seenStaff = new HashSet<string>();
//            var seenLabs = new HashSet<string>();
//            var relatedTasks = new List<TaskUnit>();
//            var (_, staffCode) = SplitStaff(task.StaffAssigned);

//            if (!string.IsNullOrEmpty(staffCode) && staffCode != "---")
//            {
//                queue.Enqueue("S:" + staffCode);
//                seenStaff.Add(staffCode);
//            }
//            if (!string.IsNullOrEmpty(task.LabId))
//            {
//                queue.Enqueue("L:" + task.LabId);
//                seenLabs.Add(task.LabId);
//            }

//            while (queue.Count > 0)
//            {
//                string item = queue.Dequeue();
//                bool isStaff = item.StartsWith("S:");
//                string code = item.Substring(2);
//                string sql;
//                NpgsqlCommand cmd;

//                if (isStaff)
//                {
//                    sql = @"
//                SELECT staff_code, subject_code, subject_name, day, hour, lab_id
//                FROM (
//                    SELECT staff_code, subject_code, subject_name, day, hour, NULL as lab_id, year, semester, department_id AS department, section
//                    FROM classtimetable
//                    UNION ALL
//                    SELECT staff_code, subject_code, subject_name, day, hour, lab_id, year, semester, department, section
//                    FROM labtimetable
//                ) AS combined
//                WHERE staff_code = ANY(@staffIds)
//                  AND year = @year
//                  AND semester = @semester
//                  AND department = @department
//                  AND section = @section
//                ORDER BY day, hour";

//                    cmd = new NpgsqlCommand(sql, conn);
//                    // Bind @staffIds as string[] array, even if single value
//                    cmd.Parameters.AddWithValue("staffIds", new string[] { code });
//                }
//                else
//                {
//                    sql = @"
//                SELECT staff_code, subject_code, subject_name, day, hour, lab_id
//                FROM labtimetable
//                WHERE lab_id = @code
//                  AND year = @year
//                  AND semester = @semester
//                  AND department = @department
//                  AND section = @section
//                ORDER BY day, hour";

//                    cmd = new NpgsqlCommand(sql, conn);
//                    cmd.Parameters.AddWithValue("code", code);
//                }

//                // Add other parameters
//                cmd.Parameters.AddWithValue("year", request.Year);
//                cmd.Parameters.AddWithValue("semester", request.Semester);
//                cmd.Parameters.AddWithValue("department", request.Department);
//                cmd.Parameters.AddWithValue("section", request.Section);

//                using var reader = await cmd.ExecuteReaderAsync();

//                while (await reader.ReadAsync())
//                {
//                    string sCode = reader.GetString(0);
//                    string subCode = reader.GetString(1);
//                    string subName = reader.GetString(2);
//                    string day = reader.GetString(3);
//                    int hour = reader.GetInt32(4);
//                    string labId = reader.IsDBNull(5) ? null : reader.GetString(5);
//                    var kind = string.IsNullOrEmpty(labId) ? "TH" : "LAB";

//                    bool exists = relatedTasks.Any(t =>
//                        t.SubjectCode == subCode && t.StaffAssigned == sCode && t.LabId == labId && t.Day == day);
//                    if (!exists)
//                    {
//                        relatedTasks.Add(new TaskUnit
//                        {
//                            SubjectCode = subCode,
//                            StaffAssigned = sCode,
//                            SubjectName = subName,
//                            LabId = labId,
//                            IsLab = !string.IsNullOrEmpty(labId),
//                            Duration = 1, // conservative; could improve by grouping hours
//                            Kind = kind,
//                            Day = day,
//                            StartHour = hour,
//                            IsPlaced = true
//                        });

//                        if (!string.IsNullOrEmpty(sCode) && sCode != "---" && !seenStaff.Contains(sCode))
//                        {
//                            queue.Enqueue("S:" + sCode);
//                            seenStaff.Add(sCode);
//                        }
//                        if (!string.IsNullOrEmpty(labId) && !seenLabs.Contains(labId))
//                        {
//                            queue.Enqueue("L:" + labId);
//                            seenLabs.Add(labId);
//                        }
//                    }
//                }
//                reader.Close();
//            }

//            return relatedTasks;
//        }


//        #endregion
//        #region ConstraintProgrammingRescheduling
//        private bool SolveConflictGroupWithCP(
//            List<TaskUnit> conflictTasks,
//            string[] days,
//            int totalHours,
//            Dictionary<string, Dictionary<string, HashSet<int>>> staffOccGlobal,
//            Dictionary<string, Dictionary<string, HashSet<int>>> labOccGlobal,
//            out Dictionary<TaskUnit, (string day, int start)> solution)
//        {
//            solution = null;
//            var dayIndex = new Dictionary<string, int>();
//            for (int i = 0; i < days.Length; i++) dayIndex[days[i]] = i;

//            var model = new CpModel();
//            int horizon = days.Length * totalHours; // total slots in the week

//            var taskVarSlots = new Dictionary<TaskUnit, IntVar>();

//            // Create variables and domain constraints
//            foreach (var task in conflictTasks)
//            {
//                var possibleSlots = new List<int>();
//                if (task.Domain != null && task.Domain.Count > 0)
//                {
//                    foreach (var (d, start) in task.Domain)
//                    {
//                        int idx = dayIndex[d] * totalHours + (start - 1);
//                        possibleSlots.Add(idx);
//                    }
//                }
//                else
//                {
//                    for (int d = 0; d < days.Length; d++)
//                        for (int start = 0; start <= totalHours - task.Duration; start++)
//                            possibleSlots.Add(d * totalHours + start);
//                }

//                if (possibleSlots.Count == 0)
//                    return false; // no available slots for this task, hence unsat

//                int min = possibleSlots.Min();
//                int max = possibleSlots.Max();

//                taskVarSlots[task] = model.NewIntVar(min, max, $"{task.SubjectCode}_{task.StaffAssigned}_slot");

//                var table = model.AddAllowedAssignments(new IntVar[] { taskVarSlots[task] });

//                // Prepare allowed slots as a 2D array for AddTuples
//                var allowedSlots = new long[possibleSlots.Count, 1];
//                for (int i = 0; i < possibleSlots.Count; i++)
//                    allowedSlots[i, 0] = possibleSlots[i];

//                table.AddTuples(allowedSlots);
//            }

//            // Create occupancy bool vars for each task-time slot
//            var taskOccupancy = new Dictionary<(TaskUnit, int), BoolVar>();
//            foreach (var task in conflictTasks)
//            {
//                for (int offset = 0; offset < task.Duration; offset++)
//                {
//                    for (int slot = 0; slot < horizon; slot++)
//                    {
//                        var b = model.NewBoolVar($"{task.SubjectCode}_covers_{slot}");
//                        taskOccupancy[(task, slot)] = b;

//                        int validSlot = slot - offset;
//                        if (validSlot >= 0)
//                        {
//                            model.Add(taskVarSlots[task] == validSlot).OnlyEnforceIf(b);
//                            model.Add(taskVarSlots[task] != validSlot).OnlyEnforceIf(b.Not());
//                        }
//                        else
//                        {
//                            model.Add(b == 0);
//                        }
//                    }
//                }
//            }

//            // Enforce no overlaps for common staff/lab
//            for (int i = 0; i < conflictTasks.Count; i++)
//            {
//                var t1 = conflictTasks[i];
//                var (_, staff1) = SplitStaff(t1.StaffAssigned);
//                var lab1 = t1.LabId;

//                for (int j = i + 1; j < conflictTasks.Count; j++)
//                {
//                    var t2 = conflictTasks[j];
//                    var (_, staff2) = SplitStaff(t2.StaffAssigned);
//                    var lab2 = t2.LabId;

//                    bool shareStaff = staff1 == staff2;
//                    bool shareLab = !string.IsNullOrEmpty(lab1) && lab1 == lab2;

//                    if (shareStaff || shareLab)
//                    {
//                        for (int slot = 0; slot < horizon; slot++)
//                            model.AddBoolOr(new[] { taskOccupancy[(t1, slot)].Not(), taskOccupancy[(t2, slot)].Not() });
//                    }
//                }
//            }

//            // Prevent overlaps with already reserved slots in global occupancy
//            foreach (var task in conflictTasks)
//            {
//                var (_, staff) = SplitStaff(task.StaffAssigned);
//                string lab = task.LabId;

//                for (int d = 0; d < days.Length; d++)
//                {
//                    string day = days[d];

//                    if (staffOccGlobal.TryGetValue(staff, out var staffDayMap) &&
//                        staffDayMap.TryGetValue(day, out var staffHours))
//                    {
//                        foreach (var hr in staffHours)
//                        {
//                            for (int offset = 0; offset < task.Duration; offset++)
//                            {
//                                int slotIdx = d * totalHours + hr - 1 - offset;
//                                if (slotIdx >= 0 && slotIdx < horizon)
//                                    model.AddBoolOr(new[] { taskOccupancy[(task, slotIdx)].Not() });
//                            }
//                        }
//                    }

//                    if (!string.IsNullOrEmpty(lab) &&
//                        labOccGlobal.TryGetValue(lab, out var labDayMap) &&
//                        labDayMap.TryGetValue(day, out var labHours))
//                    {
//                        foreach (var hr in labHours)
//                        {
//                            for (int offset = 0; offset < task.Duration; offset++)
//                            {
//                                int slotIdx = d * totalHours + hr - 1 - offset;
//                                if (slotIdx >= 0 && slotIdx < horizon)
//                                    model.AddBoolOr(new[] { taskOccupancy[(task, slotIdx)].Not() });
//                            }
//                        }
//                    }
//                }
//            }

//            // Enforce lab start time constraints (4 hours only at 1 or 4)
//            foreach (var task in conflictTasks)
//            {
//                if (task.IsLab && task.Duration == 4)
//                {
//                    var allowedOffsets = new List<int>();
//                    for (int d = 0; d < days.Length; d++)
//                    {
//                        allowedOffsets.Add(d * totalHours + 0);
//                        allowedOffsets.Add(d * totalHours + 3);
//                    }
//                    var table = model.AddAllowedAssignments(new IntVar[] { taskVarSlots[task] });
//                    var allowedSlots = new long[allowedOffsets.Count, 1];
//                    for (int i = 0; i < allowedOffsets.Count; i++)
//                        allowedSlots[i, 0] = allowedOffsets[i];
//                    table.AddTuples(allowedSlots);
//                }
//            }

//            // Embedded subject no-overlap constraints
//            var embeddedGroups = new Dictionary<string, List<TaskUnit>>();
//            foreach (var task in conflictTasks)
//            {
//                if (task.Kind.StartsWith("EMB"))
//                {
//                    if (!embeddedGroups.ContainsKey(task.SubjectCode))
//                        embeddedGroups[task.SubjectCode] = new List<TaskUnit>();
//                    embeddedGroups[task.SubjectCode].Add(task);
//                }
//            }

//            foreach (var group in embeddedGroups.Values)
//            {
//                for (int i = 0; i < group.Count; i++)
//                {
//                    for (int j = i + 1; j < group.Count; j++)
//                    {
//                        var t1 = group[i];
//                        var t2 = group[j];
//                        for (int slot = 0; slot < horizon; slot++)
//                        {
//                            model.AddBoolOr(new[] { taskOccupancy[(t1, slot)].Not(), taskOccupancy[(t2, slot)].Not() });
//                        }
//                    }
//                }
//            }

//            // Solve
//            var solver = new CpSolver
//            {
//                StringParameters = "max_time_in_seconds:10"
//            };
//            var status = solver.Solve(model);

//            if (status == CpSolverStatus.Optimal || status == CpSolverStatus.Feasible)
//            {
//                solution = new Dictionary<TaskUnit, (string, int)>();
//                foreach (var task in conflictTasks)
//                {
//                    int slot = (int)solver.Value(taskVarSlots[task]);
//                    int dayIdx = slot / totalHours;
//                    int startHour = (slot % totalHours) + 1;
//                    solution[task] = (days[dayIdx], startHour);
//                }
//                return true;
//            }
//            else
//            {
//                return false;
//            }
//        }
//        #endregion


//        [HttpPost("generateCrossDepartmentTimetableBacktracking")]
//        public async Task<IActionResult> GenerateTimetableWithCPRescheduling([FromBody] TimetableRequest request)
//        {
//            var cs = _configuration.GetConnectionString("DefaultConnection");
//            await using var conn = new NpgsqlConnection(cs);
//            await conn.OpenAsync();

//            string[] DAYS = new[] { "Mon", "Tue", "Wed", "Thu", "Fri" };
//            int HOURS = Math.Max(1, request.TotalHoursPerDay);

//            // timetable grid initialization
//            var timetableGrid = DAYS.ToDictionary(d => d, d => Enumerable.Range(1, HOURS).ToDictionary(h => h, _ => "---"));
//            var staffOcc = new Dictionary<string, Dictionary<string, HashSet<int>>>();
//            var labOcc = new Dictionary<string, Dictionary<string, HashSet<int>>>();

//            void EnsureDayMap<TKey>(Dictionary<TKey, Dictionary<string, HashSet<int>>> map, TKey key)
//            {
//                if (!map.ContainsKey(key))
//                    map[key] = DAYS.ToDictionary(d => d, _ => new HashSet<int>());
//            }

//            // Load existing staff occupancy
//            await using (var cmd = new NpgsqlCommand("SELECT staff_code, day, hour FROM classtimetable", conn))
//            await using (var rd = await cmd.ExecuteReaderAsync())
//            {
//                while (await rd.ReadAsync())
//                {
//                    var sc = rd["staff_code"] as string ?? "---";
//                    var day = rd["day"] as string ?? "Mon";
//                    var hr = (int)rd["hour"];
//                    EnsureDayMap(staffOcc, sc);
//                    staffOcc[sc][day].Add(hr);
//                }
//            }

//            // Load existing lab occupancy
//            await using (var cmd = new NpgsqlCommand("SELECT lab_id, day, hour FROM labtimetable", conn))
//            await using (var rd = await cmd.ExecuteReaderAsync())
//            {
//                while (await rd.ReadAsync())
//                {
//                    var lab = rd["lab_id"] as string ?? "---";
//                    var day = rd["day"] as string ?? "Mon";
//                    var hr = (int)rd["hour"];
//                    EnsureDayMap(labOcc, lab);
//                    labOcc[lab][day].Add(hr);
//                }
//            }

//            var subjects = request.Subjects?.Where(s => !string.IsNullOrWhiteSpace(s.StaffAssigned)).ToList();
//            if (subjects == null || subjects.Count == 0)
//                return BadRequest(new { message = "❌ No valid subjects with assigned staff found." });

//            var tasks = new List<TaskUnit>();

//            // Generate tasks from subjects
//            foreach (var s in subjects)
//            {
//                var type = (s.SubjectType ?? "theory").Trim().ToLower();
//                switch (type)
//                {
//                    case "lab":
//                        tasks.Add(new TaskUnit
//                        {
//                            SubjectCode = s.SubjectCode ?? "---",
//                            SubjectName = s.SubjectName ?? "---",
//                            StaffAssigned = s.StaffAssigned,
//                            LabId = s.LabId,
//                            IsLab = true,
//                            Duration = 4,
//                            Kind = "LAB4"
//                        });
//                        break;
//                    case "embedded":
//                        tasks.Add(new TaskUnit
//                        {
//                            SubjectCode = s.SubjectCode ?? "---",
//                            SubjectName = s.SubjectName ?? "---",
//                            StaffAssigned = s.StaffAssigned,
//                            LabId = s.LabId,
//                            IsLab = true,
//                            Duration = 2,
//                            Kind = "EMB_LAB2"
//                        });
//                        tasks.Add(new TaskUnit
//                        {
//                            SubjectCode = s.SubjectCode ?? "---",
//                            SubjectName = s.SubjectName ?? "---",
//                            StaffAssigned = s.StaffAssigned,
//                            IsLab = false,
//                            Duration = 1,
//                            Kind = "EMB_TH1"
//                        });
//                        tasks.Add(new TaskUnit
//                        {
//                            SubjectCode = s.SubjectCode ?? "---",
//                            SubjectName = s.SubjectName ?? "---",
//                            StaffAssigned = s.StaffAssigned,
//                            IsLab = false,
//                            Duration = 1,
//                            Kind = "EMB_TH1"
//                        });
//                        break;
//                    default:
//                        int count = Math.Max(0, s.Credit);
//                        for (int i = 0; i < count; i++)
//                        {
//                            tasks.Add(new TaskUnit
//                            {
//                                SubjectCode = s.SubjectCode ?? "---",
//                                SubjectName = s.SubjectName ?? "---",
//                                StaffAssigned = s.StaffAssigned,
//                                IsLab = false,
//                                Duration = 1,
//                                Kind = "TH1"
//                            });
//                        }
//                        break;
//                }
//            }

//            Shuffle(tasks);

//            // Initialize domains
//            foreach (var t in tasks)
//            {
//                t.Domain.Clear();
//                foreach (var day in DAYS)
//                {
//                    for (int start = 1; start <= HOURS - t.Duration + 1; start++)
//                    {
//                        if (IsFreeAndNoConflict(t, day, start, staffOcc, labOcc, timetableGrid, HOURS))
//                        {
//                            t.Domain.Add((day, start));
//                        }
//                    }
//                }
//                if (t.Domain.Count == 0)
//                    return Ok(new { message = $"No initial available slot for task {t.SubjectCode}.", receivedPayload = request });
//                Shuffle(t.Domain);
//            }

//            // Main backtracking with CP fallback for conflict groups
//            async Task<bool> BacktrackWithCP(List<TaskUnit> taskList)
//            {
//                if (taskList.All(t => t.IsPlaced))
//                    return true;

//                var currentTask = taskList.Where(t => !t.IsPlaced).OrderBy(t => t.Domain.Count).FirstOrDefault();
//                if (currentTask == null) return true;

//                Shuffle(currentTask.Domain);

//                foreach (var slot in currentTask.Domain)
//                {
//                    if (IsFreeAndNoConflict(currentTask, slot.day, slot.start, staffOcc, labOcc, timetableGrid, HOURS))
//                    {
//                        AssignTask(currentTask, slot, staffOcc, labOcc, timetableGrid);
//                        if (PropagateConstraints(taskList, currentTask, slot))
//                        {
//                            if (await BacktrackWithCP(taskList)) return true;
//                        }

//                        // On conflict, collect transitive related tasks and solve with CP
//                        var conflictTasks = await CollectTransitiveRelatedTasksAsync(currentTask, conn, request);

//                        // Find Tasks in current list matching these conflict tasks by primary keys (SubjectCode+Staff+Lab+Day)
//                        var conflictedTaskUnits = taskList.Where(t =>
//                            conflictTasks.Any(ct => ct.SubjectCode == t.SubjectCode && ct.StaffAssigned == t.StaffAssigned && ct.LabId == t.LabId && ct.Day == t.Day))
//                            .Where(t => t.IsPlaced).ToList();

//                        foreach (var t in conflictedTaskUnits)
//                            UnassignTask(t, staffOcc, labOcc, timetableGrid);

//                        if (conflictedTaskUnits.Count > 0)
//                        {
//                            if (SolveConflictGroupWithCP(conflictedTaskUnits, DAYS, HOURS, staffOcc, labOcc, out var cpSolution))
//                            {
//                                foreach (var (t, (d, s)) in cpSolution)
//                                {
//                                    t.Domain.Clear();
//                                    t.Domain.Add((d, s));
//                                    AssignTask(t, (d, s), staffOcc, labOcc, timetableGrid);
//                                }
//                                if (await BacktrackWithCP(taskList)) return true;
//                            }
//                            else
//                            {
//                                // No solution from CP, revert assignments
//                                foreach (var t in conflictedTaskUnits)
//                                {
//                                    AssignTask(t, (t.Day, t.StartHour), staffOcc, labOcc, timetableGrid);
//                                }
//                                UnassignTask(currentTask, staffOcc, labOcc, timetableGrid);
//                            }
//                        }
//                        else
//                        {
//                            UnassignTask(currentTask, staffOcc, labOcc, timetableGrid);
//                        }
//                    }
//                }

//                return false;
//            }

//            bool solved = await BacktrackWithCP(tasks);

//            if (!solved)
//                return Ok(new { message = "❌ Could not generate a conflict-free timetable.", receivedPayload = request });

//            // Final validation before commit
//            if (!ValidateFinalTimetable(tasks, HOURS))
//            {
//                return Ok(new { message = "❌ Timetable validation failed after generation.", receivedPayload = request });
//            }

//            // Atomic save transaction
//            await using var transaction = await conn.BeginTransactionAsync();

//            await using (var delClass = new NpgsqlCommand(@"
//                DELETE FROM classtimetable WHERE department_id=@department AND year=@year AND semester=@sem AND section=@section;", conn, transaction))
//            {
//                delClass.Parameters.AddWithValue("department", request.Department ?? "---");
//                delClass.Parameters.AddWithValue("year", request.Year ?? "---");
//                delClass.Parameters.AddWithValue("sem", request.Semester ?? "---");
//                delClass.Parameters.AddWithValue("section", request.Section ?? "---");
//                await delClass.ExecuteNonQueryAsync();
//            }

//            await using (var delLab = new NpgsqlCommand(@"
//                DELETE FROM labtimetable WHERE department=@department AND year=@year AND semester=@sem AND section=@section;", conn, transaction))
//            {
//                delLab.Parameters.AddWithValue("department", request.Department ?? "---");
//                delLab.Parameters.AddWithValue("year", request.Year ?? "---");
//                delLab.Parameters.AddWithValue("sem", request.Semester ?? "---");
//                delLab.Parameters.AddWithValue("section", request.Section ?? "---");
//                await delLab.ExecuteNonQueryAsync();
//            }

//            foreach (var t in tasks)
//            {
//                var (staffName, staffCode) = SplitStaff(t.StaffAssigned);
//                for (int h = t.StartHour; h < t.StartHour + t.Duration; h++)
//                {
//                    await using var icClass = new NpgsqlCommand(@"
//                        INSERT INTO classtimetable
//                        (staff_name, staff_code, department_id, year, semester, section, day, hour, subject_code, subject_name)
//                        VALUES
//                        (@staff_name, @staff_code, @department, @year, @semester, @section, @day, @hour, @subject_code, @subject_name);", conn, transaction);
//                    icClass.Parameters.AddWithValue("@staff_name", staffName ?? "---");
//                    icClass.Parameters.AddWithValue("@staff_code", staffCode ?? "---");
//                    icClass.Parameters.AddWithValue("@department", request.Department ?? "---");
//                    icClass.Parameters.AddWithValue("@year", request.Year ?? "---");
//                    icClass.Parameters.AddWithValue("@semester", request.Semester ?? "---");
//                    icClass.Parameters.AddWithValue("@section", request.Section ?? "---");
//                    icClass.Parameters.AddWithValue("@day", t.Day ?? "---");
//                    icClass.Parameters.AddWithValue("@hour", h);
//                    icClass.Parameters.AddWithValue("@subject_code", t.SubjectCode ?? "---");
//                    icClass.Parameters.AddWithValue("@subject_name", t.SubjectName ?? "---");
//                    await icClass.ExecuteNonQueryAsync();
//                }
//                if (t.IsLab && !string.IsNullOrEmpty(t.LabId))
//                {
//                    int labStart = t.StartHour == 4 ? 5 : t.StartHour;
//                    for (int h = labStart; h < t.StartHour + t.Duration; h++)
//                    {
//                        await using var icLab = new NpgsqlCommand(@"
//                            INSERT INTO labtimetable
//                            (lab_id, subject_code, subject_name, staff_name, department, year, semester, section, day, hour)
//                            VALUES
//                            (@lab_id, @subject_code, @subject_name, @staff_name, @department, @year, @semester, @section, @day, @hour);", conn, transaction);
//                        icLab.Parameters.AddWithValue("@lab_id", t.LabId ?? (object)DBNull.Value);
//                        icLab.Parameters.AddWithValue("@subject_code", t.SubjectCode ?? "---");
//                        icLab.Parameters.AddWithValue("@subject_name", t.SubjectName ?? "---");
//                        icLab.Parameters.AddWithValue("@staff_name", staffName ?? "---");
//                        icLab.Parameters.AddWithValue("@department", request.Department ?? "---");
//                        icLab.Parameters.AddWithValue("@year", request.Year ?? "---");
//                        icLab.Parameters.AddWithValue("@semester", request.Semester ?? "---");
//                        icLab.Parameters.AddWithValue("@section", request.Section ?? "---");
//                        icLab.Parameters.AddWithValue("@day", t.Day ?? "---");
//                        icLab.Parameters.AddWithValue("@hour", h);
//                        await icLab.ExecuteNonQueryAsync();
//                    }
//                }
//            }

//            await transaction.CommitAsync();

//            return Ok(new
//            {
//                message = "✅ Timetable generated successfully with CP-based global conflict resolution.",
//                timetable = timetableGrid.Select(t => new { Day = t.Key, Slots = t.Value }),
//                usedLabIds = tasks.Where(t => t.IsLab && !string.IsNullOrEmpty(t.LabId)).Select(t => t.LabId).Distinct(),
//                receivedPayload = request
//            });
//        }

//        private bool ValidateFinalTimetable(List<TaskUnit> tasks, int totalHours)
//        {
//            var staffSchedule = new Dictionary<string, Dictionary<string, HashSet<int>>>();
//            var labSchedule = new Dictionary<string, Dictionary<string, HashSet<int>>>();
//            foreach (var t in tasks)
//            {
//                var (_, staffCode) = SplitStaff(t.StaffAssigned);
//                if (!staffSchedule.ContainsKey(staffCode))
//                    staffSchedule[staffCode] = new Dictionary<string, HashSet<int>>();
//                if (!staffSchedule[staffCode].ContainsKey(t.Day))
//                    staffSchedule[staffCode][t.Day] = new HashSet<int>();
//                if (t.IsLab && !string.IsNullOrEmpty(t.LabId))
//                {
//                    if (!labSchedule.ContainsKey(t.LabId))
//                        labSchedule[t.LabId] = new Dictionary<string, HashSet<int>>();
//                    if (!labSchedule[t.LabId].ContainsKey(t.Day))
//                        labSchedule[t.LabId][t.Day] = new HashSet<int>();
//                }
//                for (int h = t.StartHour; h < t.StartHour + t.Duration; h++)
//                {
//                    if (h < 1 || h > totalHours)
//                        return false;
//                    if (staffSchedule[staffCode][t.Day].Contains(h))
//                        return false;
//                    else
//                        staffSchedule[staffCode][t.Day].Add(h);
//                    if (t.IsLab && !string.IsNullOrEmpty(t.LabId))
//                    {
//                        if (labSchedule[t.LabId][t.Day].Contains(h))
//                            return false;
//                        else
//                            labSchedule[t.LabId][t.Day].Add(h);
//                    }
//                    if (t.IsLab && t.Duration == 4 && !(t.StartHour == 1 || t.StartHour == 4))
//                        return false;
//                }
//            }
//            return true;
//        }
//    }
//}







//reached target
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
//        private static readonly Random rng = new Random();

//        public TimetableController(IConfiguration configuration)
//        {
//            _configuration = configuration;
//        }

//        #region DTOs
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
//            public bool IsPlaced = false;
//            public List<(string day, int start)> Domain = new();
//        }
//        #endregion

//        #region Helper Methods
//        private void Shuffle<T>(IList<T> list)
//        {
//            int n = list.Count;
//            while (n > 1)
//            {
//                n--;
//                int k = rng.Next(n + 1);
//                (list[k], list[n]) = (list[n], list[k]);
//            }
//        }

//        private (string staffName, string staffCode) SplitStaff(string staffAssigned)
//        {
//            if (string.IsNullOrWhiteSpace(staffAssigned)) return ("---", "---");
//            var name = staffAssigned;
//            var code = staffAssigned;
//            if (staffAssigned.Contains("("))
//            {
//                var parts = staffAssigned.Split('(', StringSplitOptions.TrimEntries);
//                name = parts[0].Trim();
//                code = parts.Length > 1 ? parts[1].Replace(")", "").Trim() : "---";
//            }
//            return (name, code);
//        }

//        private bool IsFreeAndNoConflict(TaskUnit t, string day, int start,
//            Dictionary<string, Dictionary<string, HashSet<int>>> staffOcc,
//            Dictionary<string, Dictionary<string, HashSet<int>>> labOcc,
//            Dictionary<string, Dictionary<int, string>> timetableGrid,
//            int totalHours)
//        {
//            for (int h = start; h < start + t.Duration; h++)
//            {
//                if (h < 1 || h > totalHours) return false;
//                if (timetableGrid[day][h] != "---") return false;
//            }
//            var (_, staffCode) = SplitStaff(t.StaffAssigned);
//            for (int h = start; h < start + t.Duration; h++)
//            {
//                if (staffOcc.TryGetValue(staffCode, out var dm) && dm.TryGetValue(day, out var set) && set.Contains(h))
//                    return false;
//            }
//            if (t.IsLab && !string.IsNullOrEmpty(t.LabId))
//            {
//                for (int h = start; h < start + t.Duration; h++)
//                {
//                    if (labOcc.TryGetValue(t.LabId, out var dm) && dm.TryGetValue(day, out var set) && set.Contains(h))
//                        return false;
//                }
//                // Specific lab constraint: 4-hour labs start only at 1 or 4
//                if (t.Kind == "LAB4" && !(start == 1 || start == 4))
//                    return false;
//            }
//            return true;
//        }

//        private void AssignTask(TaskUnit t, (string day, int start) slot,
//            Dictionary<string, Dictionary<string, HashSet<int>>> staffOcc,
//            Dictionary<string, Dictionary<string, HashSet<int>>> labOcc,
//            Dictionary<string, Dictionary<int, string>> timetableGrid)
//        {
//            var (day, start) = slot;
//            var (_, staffCode) = SplitStaff(t.StaffAssigned);
//            for (int h = start; h < start + t.Duration; h++)
//            {
//                timetableGrid[day][h] = $"{t.SubjectCode} ({t.StaffAssigned})";
//                if (!staffOcc.ContainsKey(staffCode)) staffOcc[staffCode] = new Dictionary<string, HashSet<int>>();
//                if (!staffOcc[staffCode].ContainsKey(day)) staffOcc[staffCode][day] = new HashSet<int>();
//                staffOcc[staffCode][day].Add(h);
//                if (t.IsLab && !string.IsNullOrEmpty(t.LabId))
//                {
//                    if (!labOcc.ContainsKey(t.LabId)) labOcc[t.LabId] = new Dictionary<string, HashSet<int>>();
//                    if (!labOcc[t.LabId].ContainsKey(day)) labOcc[t.LabId][day] = new HashSet<int>();
//                    labOcc[t.LabId][day].Add(h);
//                }
//            }
//            t.Day = day;
//            t.StartHour = start;
//            t.IsPlaced = true;
//        }

//        private void UnassignTask(TaskUnit t,
//            Dictionary<string, Dictionary<string, HashSet<int>>> staffOcc,
//            Dictionary<string, Dictionary<string, HashSet<int>>> labOcc,
//            Dictionary<string, Dictionary<int, string>> timetableGrid)
//        {
//            if (!t.IsPlaced) return;
//            var day = t.Day;
//            var start = t.StartHour;
//            var (_, staffCode) = SplitStaff(t.StaffAssigned);
//            for (int h = start; h < start + t.Duration; h++)
//            {
//                timetableGrid[day][h] = "---";
//                staffOcc[staffCode][day].Remove(h);
//                if (t.IsLab && !string.IsNullOrEmpty(t.LabId))
//                    labOcc[t.LabId][day].Remove(h);
//            }
//            t.IsPlaced = false;
//            t.Day = null;
//            t.StartHour = 0;
//        }

//        private bool PropagateConstraints(List<TaskUnit> tasksList, TaskUnit assignedTask, (string day, int start) assignedSlot)
//        {
//            var snapshotDomains = tasksList.ToDictionary(t => t, t => t.Domain.ToList());
//            assignedTask.Domain = new List<(string, int)> { assignedSlot };

//            foreach (var other in tasksList)
//            {
//                if (other == assignedTask || other.IsPlaced) continue;
//                var filtered = new List<(string day, int start)>();
//                foreach (var pos in other.Domain)
//                {
//                    bool conflict = false;
//                    if (pos.day == assignedSlot.day)
//                    {
//                        int start1 = pos.start;
//                        int end1 = pos.start + other.Duration - 1;
//                        int start2 = assignedSlot.start;
//                        int end2 = assignedSlot.start + assignedTask.Duration - 1;
//                        bool overlap = end1 >= start2 && end2 >= start1;
//                        if (overlap)
//                        {
//                            var (_, staff1) = SplitStaff(other.StaffAssigned);
//                            var (_, staff2) = SplitStaff(assignedTask.StaffAssigned);
//                            if (staff1 == staff2) conflict = true;

//                            if (!conflict && other.IsLab && assignedTask.IsLab &&
//                                !string.IsNullOrEmpty(other.LabId) && !string.IsNullOrEmpty(assignedTask.LabId) && other.LabId == assignedTask.LabId)
//                                conflict = true;
//                        }
//                    }
//                    if (!conflict) filtered.Add(pos);
//                }
//                other.Domain = filtered;
//                if (other.Domain.Count == 0)
//                {
//                    foreach (var kvp in snapshotDomains) kvp.Key.Domain = kvp.Value;
//                    return false;
//                }
//            }

//            return true;
//        }

//        private async Task<List<TaskUnit>> CollectRelatedTasksAsync(
//            TaskUnit task,
//            NpgsqlConnection conn,
//            TimetableRequest request,
//            Dictionary<string, Dictionary<string, HashSet<int>>> staffOcc,
//            Dictionary<string, Dictionary<string, HashSet<int>>> labOcc)
//        {
//            var relatedTasks = new List<TaskUnit>();
//            var involvedStaffIds = new HashSet<string>();
//            var involvedLabIds = new HashSet<string>();

//            var (_, staffCode) = SplitStaff(task.StaffAssigned);
//            involvedStaffIds.Add(staffCode);
//            if (!string.IsNullOrEmpty(task.LabId)) involvedLabIds.Add(task.LabId);

//            string classSql = @"
//                SELECT staff_code, subject_code, subject_name, day, hour
//                FROM classtimetable
//                WHERE staff_code = ANY(@staffIds)
//                  AND year = @year AND semester = @sem AND department_id = @dept AND section = @section
//                ORDER BY day, hour";

//            string labSql = @"
//                SELECT staff_code, subject_code, subject_name, day, hour, lab_id
//                FROM labtimetable
//                WHERE (lab_id = ANY(@labIds) OR staff_code = ANY(@staffIds))
//                  AND year = @year AND semester = @sem AND department = @dept AND section = @section
//                ORDER BY day, hour";

//            var entries = new List<(string staff_code, string subject_code, string subject_name, string day, int hour, string lab_id)>();

//            using (var cmd = new NpgsqlCommand(classSql, conn))
//            {
//                cmd.Parameters.AddWithValue("staffIds", involvedStaffIds.ToArray());
//                cmd.Parameters.AddWithValue("year", request.Year);
//                cmd.Parameters.AddWithValue("sem", request.Semester);
//                cmd.Parameters.AddWithValue("dept", request.Department);
//                cmd.Parameters.AddWithValue("section", request.Section);

//                using var reader = await cmd.ExecuteReaderAsync();
//                while (await reader.ReadAsync())
//                {
//                    entries.Add((
//                        reader.GetString(0),
//                        reader.GetString(1),
//                        reader.GetString(2),
//                        reader.GetString(3),
//                        reader.GetInt32(4),
//                        null
//                    ));
//                }
//                reader.Close();
//            }

//            using (var cmd = new NpgsqlCommand(labSql, conn))
//            {
//                cmd.Parameters.AddWithValue("labIds", involvedLabIds.Count > 0 ? involvedLabIds.ToArray() : new string[] { "" });
//                cmd.Parameters.AddWithValue("staffIds", involvedStaffIds.ToArray());
//                cmd.Parameters.AddWithValue("year", request.Year);
//                cmd.Parameters.AddWithValue("sem", request.Semester);
//                cmd.Parameters.AddWithValue("dept", request.Department);
//                cmd.Parameters.AddWithValue("section", request.Section);

//                using var reader = await cmd.ExecuteReaderAsync();
//                while (await reader.ReadAsync())
//                {
//                    entries.Add((
//                        reader.GetString(0),
//                        reader.GetString(1),
//                        reader.GetString(2),
//                        reader.GetString(3),
//                        reader.GetInt32(4),
//                        reader.IsDBNull(5) ? null : reader.GetString(5)
//                    ));
//                }
//                reader.Close();
//            }

//            var grouped = entries.GroupBy(e => (e.subject_code, e.staff_code, e.lab_id, e.day));
//            foreach (var group in grouped)
//            {
//                var hours = group.Select(e => e.hour).OrderBy(h => h).ToList();
//                int start = hours.First();
//                int duration = hours.Count;
//                relatedTasks.Add(new TaskUnit
//                {
//                    SubjectCode = group.Key.subject_code,
//                    StaffAssigned = group.Key.staff_code,
//                    SubjectName = group.First().subject_name,
//                    LabId = group.Key.lab_id,
//                    IsLab = !string.IsNullOrEmpty(group.Key.lab_id),
//                    Duration = duration,
//                    Kind = !string.IsNullOrEmpty(group.Key.lab_id) ? "LAB" : "TH",
//                    Day = group.Key.day,
//                    StartHour = start,
//                    IsPlaced = true
//                });
//            }

//            return relatedTasks;
//        }
//        #endregion

//        #region Genetic Algorithm Fallback
//        // Genetic algorithm-based local search fallback for rescheduling tasks

//        private async Task<(bool Succeeded, List<TaskUnit> Result)> RunGeneticAlgorithmAsync(
//            List<TaskUnit> tasksToAssign,
//            string[] days,
//            int hours,
//            Dictionary<string, Dictionary<string, HashSet<int>>> staffOcc,
//            Dictionary<string, Dictionary<string, HashSet<int>>> labOcc)
//        {
//            const int populationSize = 50;
//            const int maxGenerations = 150;
//            const double mutationRate = 0.15;

//            bool CanPlace(TaskUnit t, string day, int start)
//            {
//                if (start < 1 || start + t.Duration - 1 > hours)
//                    return false;

//                var (_, staffCode) = SplitStaff(t.StaffAssigned);
//                for (int h = start; h < start + t.Duration; h++)
//                {
//                    if (staffOcc.TryGetValue(staffCode, out var dayMap) && dayMap.TryGetValue(day, out var staffHours) && staffHours.Contains(h))
//                        return false;
//                    if (t.IsLab && !string.IsNullOrEmpty(t.LabId))
//                    {
//                        if (labOcc.TryGetValue(t.LabId, out var labDayMap) && labDayMap.TryGetValue(day, out var labHours) && labHours.Contains(h))
//                            return false;
//                    }
//                }
//                if (t.IsLab && t.Duration == 4 && !(start == 1 || start == 4))
//                    return false;

//                return true;
//            }

//            List<TaskUnit> CreateRandomIndividual()
//            {
//                var individual = new List<TaskUnit>();
//                foreach (var t in tasksToAssign)
//                {
//                    var validSlots = new List<(string day, int start)>();
//                    foreach (var day in days)
//                    {
//                        for (int start = 1; start <= hours - t.Duration + 1; start++)
//                        {
//                            if (CanPlace(t, day, start))
//                                validSlots.Add((day, start));
//                        }
//                    }
//                    if (validSlots.Count == 0)
//                        return null; // no valid slot
//                    var chosen = validSlots[rng.Next(validSlots.Count)];
//                    var copy = new TaskUnit
//                    {
//                        SubjectCode = t.SubjectCode,
//                        StaffAssigned = t.StaffAssigned,
//                        LabId = t.LabId,
//                        IsLab = t.IsLab,
//                        Duration = t.Duration,
//                        Day = chosen.day,
//                        StartHour = chosen.start,
//                        IsPlaced = true
//                    };
//                    individual.Add(copy);
//                }
//                return individual;
//            }

//            int Fitness(List<TaskUnit> individual)
//            {
//                int penalty = 0;
//                var staffSchedule = new Dictionary<string, Dictionary<string, HashSet<int>>>();
//                var labSchedule = new Dictionary<string, Dictionary<string, HashSet<int>>>();

//                foreach (var t in tasksToAssign)
//                {
//                    var (_, staffCode) = SplitStaff(t.StaffAssigned);
//                    if (!staffSchedule.ContainsKey(staffCode))
//                        staffSchedule[staffCode] = days.ToDictionary(d => d, _ => new HashSet<int>());
//                    if (t.IsLab && !string.IsNullOrEmpty(t.LabId) && !labSchedule.ContainsKey(t.LabId))
//                        labSchedule[t.LabId] = days.ToDictionary(d => d, _ => new HashSet<int>());
//                }

//                foreach (var t in individual)
//                {
//                    var (_, staffCode) = SplitStaff(t.StaffAssigned);
//                    for (int h = t.StartHour; h < t.StartHour + t.Duration; h++)
//                    {
//                        if (staffOcc.TryGetValue(staffCode, out var dayMap) && dayMap.TryGetValue(t.Day, out var existingHours) && existingHours.Contains(h))
//                            penalty += 10;

//                        if (t.IsLab && !string.IsNullOrEmpty(t.LabId) && labOcc.TryGetValue(t.LabId, out var labDayMap) && labDayMap.TryGetValue(t.Day, out var labExisting) && labExisting.Contains(h))
//                            penalty += 10;

//                        if (staffSchedule[staffCode][t.Day].Contains(h))
//                            penalty += 5;
//                        else
//                            staffSchedule[staffCode][t.Day].Add(h);

//                        if (t.IsLab && !string.IsNullOrEmpty(t.LabId))
//                        {
//                            if (labSchedule[t.LabId][t.Day].Contains(h))
//                                penalty += 5;
//                            else
//                                labSchedule[t.LabId][t.Day].Add(h);
//                        }
//                    }

//                    if (t.IsLab && t.Duration == 4 && !(t.StartHour == 1 || t.StartHour == 4))
//                        penalty += 20;
//                }

//                return penalty;
//            }

//            List<TaskUnit> TournamentSelection(List<List<TaskUnit>> population)
//            {
//                int k = 3;
//                var selected = new List<List<TaskUnit>>();
//                for (int i = 0; i < k; i++)
//                    selected.Add(population[rng.Next(population.Count)]);
//                return selected.OrderBy(ind => Fitness(ind)).First();
//            }

//            (List<TaskUnit>, List<TaskUnit>) Crossover(List<TaskUnit> parent1, List<TaskUnit> parent2)
//            {
//                int point = rng.Next(1, parent1.Count);
//                var child1 = new List<TaskUnit>();
//                var child2 = new List<TaskUnit>();
//                for (int i = 0; i < parent1.Count; i++)
//                {
//                    child1.Add(i < point ? parent1[i] : parent2[i]);
//                    child2.Add(i < point ? parent2[i] : parent1[i]);
//                }
//                return (child1, child2);
//            }

//            void Mutate(List<TaskUnit> individual)
//            {
//                for (int i = 0; i < individual.Count; i++)
//                {
//                    if (rng.NextDouble() < mutationRate)
//                    {
//                        var t = tasksToAssign[i];
//                        var validSlots = new List<(string day, int start)>();
//                        foreach (var day in days)
//                        {
//                            for (int start = 1; start <= hours - t.Duration + 1; start++)
//                            {
//                                if (CanPlace(t, day, start))
//                                    validSlots.Add((day, start));
//                            }
//                        }
//                        if (validSlots.Count > 0)
//                        {
//                            var chosen = validSlots[rng.Next(validSlots.Count)];
//                            individual[i].Day = chosen.day;
//                            individual[i].StartHour = chosen.start;
//                        }
//                    }
//                }
//            }

//            var population = new List<List<TaskUnit>>();
//            for (int i = 0; i < populationSize; i++)
//            {
//                var individual = CreateRandomIndividual();
//                if (individual != null)
//                    population.Add(individual);
//            }
//            if (population.Count == 0)
//                return (false, null);

//            for (int gen = 0; gen < maxGenerations; gen++)
//            {
//                population = population.OrderBy(ind => Fitness(ind)).ToList();
//                var best = population[0];
//                if (Fitness(best) == 0)
//                {
//                    return (true, best);
//                }
//                var nextGen = new List<List<TaskUnit>>
//                {
//                    population[0], population[1]
//                };
//                while (nextGen.Count < populationSize)
//                {
//                    var parent1 = TournamentSelection(population);
//                    var parent2 = TournamentSelection(population);
//                    var (child1, child2) = Crossover(parent1, parent2);
//                    Mutate(child1);
//                    Mutate(child2);
//                    nextGen.Add(child1);
//                    if (nextGen.Count < populationSize)
//                        nextGen.Add(child2);
//                }
//                population = nextGen;
//            }
//            population = population.OrderBy(ind => Fitness(ind)).ToList();
//            if (Fitness(population[0]) == 0)
//                return (true, population[0]);
//            return (false, null);
//        }
//        #endregion

//        #region Timetable Generation Endpoint

//        [HttpPost("generateCrossDepartmentTimetableBacktracking")]
//        public async Task<IActionResult> GenerateTimetableWithGARescheduling([FromBody] TimetableRequest request)
//        {
//            var cs = _configuration.GetConnectionString("DefaultConnection");
//            await using var conn = new NpgsqlConnection(cs);
//            await conn.OpenAsync();

//            string[] DAYS = new[] { "Mon", "Tue", "Wed", "Thu", "Fri" };
//            int HOURS = Math.Max(1, request.TotalHoursPerDay);

//            // Initialize timetable grid with empty slots
//            var timetableGrid = DAYS.ToDictionary(d => d, d => Enumerable.Range(1, HOURS).ToDictionary(h => h, _ => "---"));

//            var staffOcc = new Dictionary<string, Dictionary<string, HashSet<int>>>();
//            var labOcc = new Dictionary<string, Dictionary<string, HashSet<int>>>();

//            void EnsureDayMap<TKey>(Dictionary<TKey, Dictionary<string, HashSet<int>>> map, TKey key)
//            {
//                if (!map.ContainsKey(key))
//                    map[key] = DAYS.ToDictionary(d => d, _ => new HashSet<int>());
//            }

//            // Load existing staff occupancy from DB
//            await using (var cmd = new NpgsqlCommand("SELECT staff_code, day, hour FROM classtimetable", conn))
//            await using (var rd = await cmd.ExecuteReaderAsync())
//            {
//                while (await rd.ReadAsync())
//                {
//                    var sc = rd["staff_code"] as string ?? "---";
//                    var day = rd["day"] as string ?? "Mon";
//                    var hr = (int)rd["hour"];
//                    EnsureDayMap(staffOcc, sc);
//                    staffOcc[sc][day].Add(hr);
//                }
//            }

//            // Load existing lab occupancy from DB
//            await using (var cmd = new NpgsqlCommand("SELECT lab_id, day, hour FROM labtimetable", conn))
//            await using (var rd = await cmd.ExecuteReaderAsync())
//            {
//                while (await rd.ReadAsync())
//                {
//                    var lab = rd["lab_id"] as string ?? "---";
//                    var day = rd["day"] as string ?? "Mon";
//                    var hr = (int)rd["hour"];
//                    EnsureDayMap(labOcc, lab);
//                    labOcc[lab][day].Add(hr);
//                }
//            }

//            var subjects = request.Subjects?.Where(s => !string.IsNullOrWhiteSpace(s.StaffAssigned)).ToList();
//            if (subjects == null || subjects.Count == 0)
//                return BadRequest(new { message = "❌ No valid subjects with assigned staff found." });

//            // Create Tasks for scheduling
//            var tasks = new List<TaskUnit>();
//            foreach (var s in subjects)
//            {
//                var type = (s.SubjectType ?? "theory").Trim().ToLower();
//                switch (type)
//                {
//                    case "lab":
//                        tasks.Add(new TaskUnit
//                        {
//                            SubjectCode = s.SubjectCode ?? "---",
//                            SubjectName = s.SubjectName ?? "---",
//                            StaffAssigned = s.StaffAssigned,
//                            LabId = s.LabId,
//                            IsLab = true,
//                            Duration = 4,
//                            Kind = "LAB4"
//                        });
//                        break;
//                    case "embedded":
//                        tasks.Add(new TaskUnit
//                        {
//                            SubjectCode = s.SubjectCode ?? "---",
//                            SubjectName = s.SubjectName ?? "---",
//                            StaffAssigned = s.StaffAssigned,
//                            LabId = s.LabId,
//                            IsLab = true,
//                            Duration = 2,
//                            Kind = "EMB_LAB2"
//                        });
//                        tasks.Add(new TaskUnit
//                        {
//                            SubjectCode = s.SubjectCode ?? "---",
//                            SubjectName = s.SubjectName ?? "---",
//                            StaffAssigned = s.StaffAssigned,
//                            IsLab = false,
//                            Duration = 1,
//                            Kind = "EMB_TH1"
//                        });
//                        tasks.Add(new TaskUnit
//                        {
//                            SubjectCode = s.SubjectCode ?? "---",
//                            SubjectName = s.SubjectName ?? "---",
//                            StaffAssigned = s.StaffAssigned,
//                            IsLab = false,
//                            Duration = 1,
//                            Kind = "EMB_TH1"
//                        });
//                        break;
//                    default:
//                        int count = Math.Max(0, s.Credit);
//                        for (int i = 0; i < count; i++)
//                        {
//                            tasks.Add(new TaskUnit
//                            {
//                                SubjectCode = s.SubjectCode ?? "---",
//                                SubjectName = s.SubjectName ?? "---",
//                                StaffAssigned = s.StaffAssigned,
//                                IsLab = false,
//                                Duration = 1,
//                                Kind = "TH1"
//                            });
//                        }
//                        break;
//                }
//            }

//            Shuffle(tasks);

//            // Initialize each task's domain with all feasible slots
//            foreach (var t in tasks)
//            {
//                t.Domain.Clear();
//                foreach (var day in DAYS)
//                {
//                    for (int start = 1; start <= HOURS - t.Duration + 1; start++)
//                    {
//                        if (IsFreeAndNoConflict(t, day, start, staffOcc, labOcc, timetableGrid, HOURS))
//                        {
//                            t.Domain.Add((day, start));
//                        }
//                    }
//                }
//                if (t.Domain.Count == 0)
//                {
//                    Console.WriteLine($"No initial available slot for task {t.SubjectCode}.");
//                    return Ok(new { message = $"No initial available slot for task {t.SubjectCode}.", receivedPayload = request });
//                }
//                Shuffle(t.Domain);
//            }

//            // Enhanced backtracking with recursive rescheduling and integrated local search fallback
//            async Task<bool> BacktrackWithRecursiveReschedulingAsync(
//                List<TaskUnit> allTasks)
//            {
//                if (allTasks.All(t => t.IsPlaced))
//                    return true;

//                // MRV heuristic: pick task with fewest remaining options
//                var currentTask = allTasks.Where(t => !t.IsPlaced)
//                                          .OrderBy(t => t.Domain.Count)
//                                          .FirstOrDefault();

//                if (currentTask == null)
//                    return true;

//                var domainCopy = currentTask.Domain.OrderBy(_ => rng.Next()).ToList();

//                foreach (var slot in domainCopy)
//                {
//                    if (!IsFreeAndNoConflict(currentTask, slot.day, slot.start, staffOcc, labOcc, timetableGrid, HOURS))
//                        continue;

//                    AssignTask(currentTask, slot, staffOcc, labOcc, timetableGrid);

//                    bool propagateOk = PropagateConstraints(allTasks, currentTask, slot);

//                    if (propagateOk)
//                    {
//                        bool solved = await BacktrackWithRecursiveReschedulingAsync(allTasks);
//                        if (solved)
//                            return true;
//                    }

//                    // On failure, rollback current assignment
//                    UnassignTask(currentTask, staffOcc, labOcc, timetableGrid);

//                    // Collect related conflicting tasks for rescheduling
//                    var conflictTasks = await CollectRelatedTasksAsync(currentTask, conn, request, staffOcc, labOcc);

//                    var rescheduleTasks = new List<TaskUnit> { currentTask };
//                    foreach (var t in conflictTasks)
//                        if (!rescheduleTasks.Contains(t))
//                            rescheduleTasks.Add(t);

//                    // Clear assignments & rebuild domains for reschedule tasks
//                    foreach (var t in rescheduleTasks)
//                    {
//                        UnassignTask(t, staffOcc, labOcc, timetableGrid);
//                        t.IsPlaced = false;
//                        t.Domain.Clear();
//                        foreach (var d in DAYS)
//                        {
//                            for (int st = 1; st <= HOURS - t.Duration + 1; st++)
//                            {
//                                if (IsFreeAndNoConflict(t, d, st, staffOcc, labOcc, timetableGrid, HOURS))
//                                    t.Domain.Add((d, st));
//                            }
//                        }
//                        if (t.Domain.Count == 0)
//                        {
//                            // early prune - no slots available for reschedule task
//                            foreach (var ut in rescheduleTasks)
//                                if (ut.IsPlaced)
//                                    UnassignTask(ut, staffOcc, labOcc, timetableGrid);
//                            UnassignTask(currentTask, staffOcc, labOcc, timetableGrid);
//                            return false;
//                        }
//                    }

//                    // Run GA fallback on reschedule tasks
//                    var gaResult = await RunGeneticAlgorithmAsync(rescheduleTasks, DAYS, HOURS, staffOcc, labOcc);

//                    if (gaResult.Succeeded)
//                    {
//                        // Apply GA solution assignments
//                        foreach (var t in rescheduleTasks)
//                        {
//                            t.Domain.Clear();
//                            t.Domain.Add((t.Day, t.StartHour));
//                            AssignTask(t, (t.Day, t.StartHour), staffOcc, labOcc, timetableGrid);
//                        }

//                        // Retry backtracking after GA fix
//                        if (await BacktrackWithRecursiveReschedulingAsync(allTasks))
//                            return true;
//                        else
//                        {
//                            foreach (var t in rescheduleTasks)
//                                UnassignTask(t, staffOcc, labOcc, timetableGrid);
//                        }
//                    }
//                    else
//                    {
//                        foreach (var t in rescheduleTasks)
//                            UnassignTask(t, staffOcc, labOcc, timetableGrid);
//                    }
//                }

//                return false;
//            }

//            bool solved = await BacktrackWithRecursiveReschedulingAsync(tasks);

//            if (!solved)
//                return Ok(new { message = "❌ Could not generate a conflict-free timetable.", receivedPayload = request });

//            // Final consistency validation
//            if (!ValidateFinalTimetable(tasks, HOURS))
//            {
//                return Ok(new { message = "❌ Timetable validation failed after generation.", receivedPayload = request });
//            }

//            // Atomic DB save transaction
//            await using var transaction = await conn.BeginTransactionAsync();

//            // Clear old timetable entries
//            await using (var delClass = new NpgsqlCommand(@"
//                DELETE FROM classtimetable WHERE department_id=@department AND year=@year AND semester=@sem AND section=@section;", conn, transaction))
//            {
//                delClass.Parameters.AddWithValue("department", request.Department ?? "---");
//                delClass.Parameters.AddWithValue("year", request.Year ?? "---");
//                delClass.Parameters.AddWithValue("sem", request.Semester ?? "---");
//                delClass.Parameters.AddWithValue("section", request.Section ?? "---");
//                await delClass.ExecuteNonQueryAsync();
//            }

//            await using (var delLab = new NpgsqlCommand(@"
//                DELETE FROM labtimetable WHERE department=@department AND year=@year AND semester=@sem AND section=@section;", conn, transaction))
//            {
//                delLab.Parameters.AddWithValue("department", request.Department ?? "---");
//                delLab.Parameters.AddWithValue("year", request.Year ?? "---");
//                delLab.Parameters.AddWithValue("sem", request.Semester ?? "---");
//                delLab.Parameters.AddWithValue("section", request.Section ?? "---");
//                await delLab.ExecuteNonQueryAsync();
//            }

//            // Insert new timetable data
//            foreach (var t in tasks)
//            {
//                var (staffName, staffCode) = SplitStaff(t.StaffAssigned);
//                for (int h = t.StartHour; h < t.StartHour + t.Duration; h++)
//                {
//                    await using var icClass = new NpgsqlCommand(@"
//                        INSERT INTO classtimetable
//                        (staff_name, staff_code, department_id, year, semester, section, day, hour, subject_code, subject_name)
//                        VALUES
//                        (@staff_name, @staff_code, @department, @year, @semester, @section, @day, @hour, @subject_code, @subject_name);", conn, transaction);
//                    icClass.Parameters.AddWithValue("@staff_name", staffName ?? "---");
//                    icClass.Parameters.AddWithValue("@staff_code", staffCode ?? "---");
//                    icClass.Parameters.AddWithValue("@department", request.Department ?? "---");
//                    icClass.Parameters.AddWithValue("@year", request.Year ?? "---");
//                    icClass.Parameters.AddWithValue("@semester", request.Semester ?? "---");
//                    icClass.Parameters.AddWithValue("@section", request.Section ?? "---");
//                    icClass.Parameters.AddWithValue("@day", t.Day ?? "---");
//                    icClass.Parameters.AddWithValue("@hour", h);
//                    icClass.Parameters.AddWithValue("@subject_code", t.SubjectCode ?? "---");
//                    icClass.Parameters.AddWithValue("@subject_name", t.SubjectName ?? "---");
//                    await icClass.ExecuteNonQueryAsync();
//                }

//                if (t.IsLab && !string.IsNullOrEmpty(t.LabId))
//                {
//                    int labStart = t.StartHour == 4 ? 5 : t.StartHour;
//                    for (int h = labStart; h < t.StartHour + t.Duration; h++)
//                    {
//                        await using var icLab = new NpgsqlCommand(@"
//                            INSERT INTO labtimetable
//                            (lab_id, subject_code, subject_name, staff_name, department, year, semester, section, day, hour)
//                            VALUES
//                            (@lab_id, @subject_code, @subject_name, @staff_name, @department, @year, @semester, @section, @day, @hour);", conn, transaction);
//                        icLab.Parameters.AddWithValue("@lab_id", t.LabId ?? (object)DBNull.Value);
//                        icLab.Parameters.AddWithValue("@subject_code", t.SubjectCode ?? "---");
//                        icLab.Parameters.AddWithValue("@subject_name", t.SubjectName ?? "---");
//                        icLab.Parameters.AddWithValue("@staff_name", staffName ?? "---");
//                        icLab.Parameters.AddWithValue("@department", request.Department ?? "---");
//                        icLab.Parameters.AddWithValue("@year", request.Year ?? "---");
//                        icLab.Parameters.AddWithValue("@semester", request.Semester ?? "---");
//                        icLab.Parameters.AddWithValue("@section", request.Section ?? "---");
//                        icLab.Parameters.AddWithValue("@day", t.Day ?? "---");
//                        icLab.Parameters.AddWithValue("@hour", h);
//                        await icLab.ExecuteNonQueryAsync();
//                    }
//                }
//            }

//            await transaction.CommitAsync();

//            return Ok(new
//            {
//                message = "✅ Timetable generated successfully with enhanced global conflict resolution.",
//                timetable = timetableGrid.Select(t => new { Day = t.Key, Slots = t.Value }),
//                usedLabIds = tasks.Where(t => t.IsLab && !string.IsNullOrEmpty(t.LabId)).Select(t => t.LabId).Distinct(),
//                receivedPayload = request
//            });
//        }

//        private bool ValidateFinalTimetable(List<TaskUnit> tasks, int totalHours)
//        {
//            var staffSchedule = new Dictionary<string, Dictionary<string, HashSet<int>>>();
//            var labSchedule = new Dictionary<string, Dictionary<string, HashSet<int>>>();

//            foreach (var t in tasks)
//            {
//                var (_, staffCode) = SplitStaff(t.StaffAssigned);
//                if (!staffSchedule.ContainsKey(staffCode))
//                    staffSchedule[staffCode] = new Dictionary<string, HashSet<int>>();
//                if (!staffSchedule[staffCode].ContainsKey(t.Day))
//                    staffSchedule[staffCode][t.Day] = new HashSet<int>();

//                if (t.IsLab && !string.IsNullOrEmpty(t.LabId))
//                {
//                    if (!labSchedule.ContainsKey(t.LabId))
//                        labSchedule[t.LabId] = new Dictionary<string, HashSet<int>>();
//                    if (!labSchedule[t.LabId].ContainsKey(t.Day))
//                        labSchedule[t.LabId][t.Day] = new HashSet<int>();
//                }

//                for (int h = t.StartHour; h < t.StartHour + t.Duration; h++)
//                {
//                    if (h < 1 || h > totalHours)
//                        return false;

//                    if (staffSchedule[staffCode][t.Day].Contains(h))
//                        return false;
//                    else
//                        staffSchedule[staffCode][t.Day].Add(h);

//                    if (t.IsLab && !string.IsNullOrEmpty(t.LabId))
//                    {
//                        if (labSchedule[t.LabId][t.Day].Contains(h))
//                            return false;
//                        else
//                            labSchedule[t.LabId][t.Day].Add(h);
//                    }

//                    // Lab block constraints
//                    if (t.IsLab && t.Duration == 4 && !(t.StartHour == 1 || t.StartHour == 4))
//                        return false;
//                }
//            }
//            return true;
//        }
//        #endregion
//    }
//}




//SIH presenented code
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
//        private static readonly Random rng = new Random();
//        private const int MAX_RECURSION_DEPTH = 900;

//        public TimetableController(IConfiguration configuration)
//        {
//            _configuration = configuration;
//        }

//        #region DTOs

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
//            public bool IsPlaced = false;
//            public List<(string day, int start)> Domain = new();
//            public List<TaskUnit> Conflicts = new();
//        }

//        #endregion

//        #region Helper Methods

//        private void Shuffle<T>(IList<T> list)
//        {
//            int n = list.Count;
//            while (n > 1)
//            {
//                n--;
//                int k = rng.Next(n + 1);
//                (list[k], list[n]) = (list[n], list[k]);
//            }
//        }

//        private (string staffName, string staffCode) SplitStaff(string staffAssigned)
//        {
//            if (string.IsNullOrWhiteSpace(staffAssigned))
//                return ("---", "---");
//            var name = staffAssigned;
//            var code = staffAssigned;
//            if (staffAssigned.Contains("("))
//            {
//                var parts = staffAssigned.Split('(', StringSplitOptions.TrimEntries);
//                name = parts[0].Trim();
//                code = parts.Length > 1 ? parts[1].Replace(")", "").Trim() : "---";
//            }

//            if (string.IsNullOrEmpty(code))
//                code = "---";

//            return (name, code);
//        }


//        private bool IsFreeAndNoConflict(TaskUnit t, string day, int start,
//            Dictionary<string, Dictionary<string, HashSet<int>>> staffOcc,
//            Dictionary<string, Dictionary<string, HashSet<int>>> labOcc,
//            Dictionary<string, Dictionary<int, string>> timetableGrid,
//            int totalHours)
//        {
//            for (int h = start; h < start + t.Duration; h++)
//            {
//                if (h < 1 || h > totalHours) return false;
//                if (timetableGrid[day][h] != "---") return false;
//            }
//            var (_, staffCode) = SplitStaff(t.StaffAssigned);
//            for (int h = start; h < start + t.Duration; h++)
//            {
//                if (staffOcc.TryGetValue(staffCode, out var dm) && dm.TryGetValue(day, out var set) && set.Contains(h))
//                    return false;
//            }
//            if (t.IsLab && !string.IsNullOrEmpty(t.LabId))
//            {
//                for (int h = start; h < start + t.Duration; h++)
//                {
//                    if (labOcc.TryGetValue(t.LabId, out var dm) && dm.TryGetValue(day, out var set) && set.Contains(h))
//                        return false;
//                }
//                if (t.Kind == "LAB4" && !(start == 1 || start == 4))
//                    return false;
//            }
//            return true;
//        }

//        private void AssignTask(TaskUnit t, (string day, int start) slot,
//            Dictionary<string, Dictionary<string, HashSet<int>>> staffOcc,
//            Dictionary<string, Dictionary<string, HashSet<int>>> labOcc,
//            Dictionary<string, Dictionary<int, string>> timetableGrid)
//        {
//            var (day, start) = slot;
//            var (_, staffCode) = SplitStaff(t.StaffAssigned);
//            if (day == null || staffCode == null)
//            {
//                Console.WriteLine($"Error: Attempted to assign with null key. day: {day}, staffCode: {staffCode}");
//                return;
//            }
//            for (int h = start; h < start + t.Duration; h++)
//            {
//                if (!timetableGrid.ContainsKey(day) || timetableGrid[day] == null)
//                {
//                    Console.WriteLine($"Error: Timetable grid missing for day: {day}");
//                    continue;
//                }
//                timetableGrid[day][h] = $"{t.SubjectCode} ({t.StaffAssigned})";
//                if (!staffOcc.ContainsKey(staffCode)) staffOcc[staffCode] = new Dictionary<string, HashSet<int>>();
//                if (!staffOcc[staffCode].ContainsKey(day)) staffOcc[staffCode][day] = new HashSet<int>();
//                staffOcc[staffCode][day].Add(h);
//                if (t.IsLab && !string.IsNullOrEmpty(t.LabId))
//                {
//                    if (!labOcc.ContainsKey(t.LabId)) labOcc[t.LabId] = new Dictionary<string, HashSet<int>>();
//                    if (!labOcc[t.LabId].ContainsKey(day)) labOcc[t.LabId][day] = new HashSet<int>();
//                    labOcc[t.LabId][day].Add(h);
//                }
//            }
//            t.Day = day;
//            t.StartHour = start;
//            t.IsPlaced = true;
//        }

//        private void UnassignTask(TaskUnit t,
//            Dictionary<string, Dictionary<string, HashSet<int>>> staffOcc,
//            Dictionary<string, Dictionary<string, HashSet<int>>> labOcc,
//            Dictionary<string, Dictionary<int, string>> timetableGrid)
//        {
//            if (!t.IsPlaced) return;
//            var day = t.Day;
//            var start = t.StartHour;
//            var (_, staffCode) = SplitStaff(t.StaffAssigned);
//            for (int h = start; h < start + t.Duration; h++)
//            {
//                timetableGrid[day][h] = "---";
//                staffOcc[staffCode][day].Remove(h);
//                if (t.IsLab && !string.IsNullOrEmpty(t.LabId))
//                    labOcc[t.LabId][day].Remove(h);
//            }
//            t.IsPlaced = false;
//            t.Day = null;
//            t.StartHour = 0;
//        }

//        private bool PropagateConstraints(List<TaskUnit> tasksList, TaskUnit assignedTask, (string day, int start) assignedSlot)
//        {
//            var snapshotDomains = tasksList.ToDictionary(t => t, t => t.Domain.ToList());
//            assignedTask.Domain = new List<(string, int)> { assignedSlot };

//            foreach (var other in tasksList)
//            {
//                if (other == assignedTask || other.IsPlaced) continue;
//                var filtered = new List<(string day, int start)>();
//                foreach (var pos in other.Domain)
//                {
//                    bool conflict = false;
//                    if (pos.day == assignedSlot.day)
//                    {
//                        int start1 = pos.start;
//                        int end1 = pos.start + other.Duration - 1;
//                        int start2 = assignedSlot.start;
//                        int end2 = assignedSlot.start + assignedTask.Duration - 1;
//                        bool overlap = end1 >= start2 && end2 >= start1;
//                        if (overlap)
//                        {
//                            var (_, staff1) = SplitStaff(other.StaffAssigned);
//                            var (_, staff2) = SplitStaff(assignedTask.StaffAssigned);
//                            if (staff1 == staff2) conflict = true;

//                            if (!conflict && other.IsLab && assignedTask.IsLab &&
//                                !string.IsNullOrEmpty(other.LabId) && !string.IsNullOrEmpty(assignedTask.LabId) && other.LabId == assignedTask.LabId)
//                                conflict = true;
//                        }
//                    }
//                    if (!conflict) filtered.Add(pos);
//                }
//                other.Domain = filtered;
//                if (other.Domain.Count == 0)
//                {
//                    foreach (var kvp in snapshotDomains) kvp.Key.Domain = kvp.Value;
//                    return false;
//                }
//            }

//            return true;
//        }

//        private void BuildConflictGraph(List<TaskUnit> tasks)
//        {
//            foreach (var t in tasks) t.Conflicts.Clear();

//            for (int i = 0; i < tasks.Count; i++)
//            {
//                for (int j = i + 1; j < tasks.Count; j++)
//                {
//                    var a = tasks[i];
//                    var b = tasks[j];
//                    if (HaveConflict(a, b))
//                    {
//                        a.Conflicts.Add(b);
//                        b.Conflicts.Add(a);
//                    }
//                }
//            }
//        }

//        private bool HaveConflict(TaskUnit a, TaskUnit b)
//        {
//            var (_, staffA) = SplitStaff(a.StaffAssigned);
//            var (_, staffB) = SplitStaff(b.StaffAssigned);

//            if (staffA == staffB) return true;

//            if (a.IsLab && b.IsLab && !string.IsNullOrEmpty(a.LabId) && !string.IsNullOrEmpty(b.LabId) && a.LabId == b.LabId)
//                return true;

//            foreach (var posA in a.Domain)
//                foreach (var posB in b.Domain)
//                    if (posA.day == posB.day)
//                    {
//                        int startA = posA.start;
//                        int endA = startA + a.Duration - 1;
//                        int startB = posB.start;
//                        int endB = startB + b.Duration - 1;
//                        if (endA >= startB && endB >= startA) return true;
//                    }

//            return false;
//        }

//        private List<TaskUnit> GetConflictComponent(TaskUnit task)
//        {
//            var visited = new HashSet<TaskUnit>();
//            var stack = new Stack<TaskUnit>();
//            var component = new List<TaskUnit>();

//            stack.Push(task);

//            while (stack.Count > 0)
//            {
//                var current = stack.Pop();
//                if (visited.Contains(current)) continue;
//                visited.Add(current);
//                component.Add(current);

//                foreach (var neighbor in current.Conflicts)
//                    if (!visited.Contains(neighbor))
//                        stack.Push(neighbor);
//            }

//            return component;
//        }

//        #endregion

//        #region Genetic Algorithm Fallback

//        private async Task<(bool Succeeded, List<TaskUnit> Result)> RunGeneticAlgorithmAsync(
//            List<TaskUnit> tasksToAssign,
//            string[] days,
//            int hours,
//            Dictionary<string, Dictionary<string, HashSet<int>>> staffOcc,
//            Dictionary<string, Dictionary<string, HashSet<int>>> labOcc)
//        {
//            const int populationSize = 50;
//            const int maxGenerations = 150;
//            const double mutationRate = 0.15;

//            bool CanPlace(TaskUnit t, string day, int start)
//            {
//                if (start < 1 || start + t.Duration - 1 > hours)
//                    return false;

//                var (_, staffCode) = SplitStaff(t.StaffAssigned);
//                for (int h = start; h < start + t.Duration; h++)
//                {
//                    if (staffOcc.TryGetValue(staffCode, out var dayMap) && dayMap.TryGetValue(day, out var staffHours) && staffHours.Contains(h))
//                        return false;
//                    if (t.IsLab && !string.IsNullOrEmpty(t.LabId))
//                    {
//                        if (labOcc.TryGetValue(t.LabId, out var labDayMap) && labDayMap.TryGetValue(day, out var labHours) && labHours.Contains(h))
//                            return false;
//                    }
//                }
//                if (t.IsLab && t.Duration == 4 && !(start == 1 || start == 4))
//                    return false;

//                return true;
//            }

//            List<TaskUnit> CreateRandomIndividual()
//            {
//                var individual = new List<TaskUnit>();
//                foreach (var t in tasksToAssign)
//                {
//                    var validSlots = new List<(string day, int start)>();
//                    foreach (var day in days)
//                    {
//                        for (int start = 1; start <= hours - t.Duration + 1; start++)
//                        {
//                            if (CanPlace(t, day, start))
//                                validSlots.Add((day, start));
//                        }
//                    }
//                    if (validSlots.Count == 0)
//                        return null;
//                    var chosen = validSlots[rng.Next(validSlots.Count)];
//                    var copy = new TaskUnit
//                    {
//                        SubjectCode = t.SubjectCode,
//                        StaffAssigned = t.StaffAssigned,
//                        LabId = t.LabId,
//                        IsLab = t.IsLab,
//                        Duration = t.Duration,
//                        Day = chosen.day,
//                        StartHour = chosen.start,
//                        IsPlaced = true
//                    };
//                    individual.Add(copy);
//                }
//                return individual;
//            }

//            int Fitness(List<TaskUnit> individual)
//            {
//                int penalty = 0;
//                var staffSchedule = new Dictionary<string, Dictionary<string, HashSet<int>>>();
//                var labSchedule = new Dictionary<string, Dictionary<string, HashSet<int>>>();

//                foreach (var t in tasksToAssign)
//                {
//                    var (_, staffCode) = SplitStaff(t.StaffAssigned);
//                    if (!staffSchedule.ContainsKey(staffCode))
//                        staffSchedule[staffCode] = days.ToDictionary(d => d, _ => new HashSet<int>());
//                    if (t.IsLab && !string.IsNullOrEmpty(t.LabId) && !labSchedule.ContainsKey(t.LabId))
//                        labSchedule[t.LabId] = days.ToDictionary(d => d, _ => new HashSet<int>());
//                }

//                foreach (var t in individual)
//                {
//                    var (_, staffCode) = SplitStaff(t.StaffAssigned);
//                    for (int h = t.StartHour; h < t.StartHour + t.Duration; h++)
//                    {
//                        if (staffOcc.TryGetValue(staffCode, out var dayMap) && dayMap.TryGetValue(t.Day, out var existingHours) && existingHours.Contains(h))
//                            penalty += 10;

//                        if (t.IsLab && !string.IsNullOrEmpty(t.LabId) && labOcc.TryGetValue(t.LabId, out var labDayMap) && labDayMap.TryGetValue(t.Day, out var labExisting) && labExisting.Contains(h))
//                            penalty += 10;

//                        if (staffSchedule[staffCode][t.Day].Contains(h))
//                            penalty += 5;
//                        else
//                            staffSchedule[staffCode][t.Day].Add(h);

//                        if (t.IsLab && !string.IsNullOrEmpty(t.LabId))
//                        {
//                            if (labSchedule[t.LabId][t.Day].Contains(h))
//                                penalty += 5;
//                            else
//                                labSchedule[t.LabId][t.Day].Add(h);
//                        }
//                    }

//                    if (t.IsLab && t.Duration == 4 && !(t.StartHour == 1 || t.StartHour == 4))
//                        penalty += 20;
//                }
//                return penalty;
//            }

//            List<TaskUnit> TournamentSelection(List<List<TaskUnit>> population)
//            {
//                int k = 3;
//                var selected = new List<List<TaskUnit>>();
//                for (int i = 0; i < k; i++)
//                    selected.Add(population[rng.Next(population.Count)]);
//                return selected.OrderBy(ind => Fitness(ind)).First();
//            }

//            (List<TaskUnit>, List<TaskUnit>) Crossover(List<TaskUnit> parent1, List<TaskUnit> parent2)
//            {
//                int point = rng.Next(1, parent1.Count);
//                var child1 = new List<TaskUnit>();
//                var child2 = new List<TaskUnit>();
//                for (int i = 0; i < parent1.Count; i++)
//                {
//                    child1.Add(i < point ? parent1[i] : parent2[i]);
//                    child2.Add(i < point ? parent2[i] : parent1[i]);
//                }
//                return (child1, child2);
//            }

//            void Mutate(List<TaskUnit> individual)
//            {
//                for (int i = 0; i < individual.Count; i++)
//                {
//                    if (rng.NextDouble() < mutationRate)
//                    {
//                        var t = tasksToAssign[i];
//                        var validSlots = new List<(string day, int start)>();
//                        foreach (var day in days)
//                        {
//                            for (int start = 1; start <= hours - t.Duration + 1; start++)
//                            {
//                                if (CanPlace(t, day, start))
//                                    validSlots.Add((day, start));
//                            }
//                        }
//                        if (validSlots.Count > 0)
//                        {
//                            var chosen = validSlots[rng.Next(validSlots.Count)];
//                            individual[i].Day = chosen.day;
//                            individual[i].StartHour = chosen.start;
//                        }
//                    }
//                }
//            }

//            var population = new List<List<TaskUnit>>();
//            for (int i = 0; i < populationSize; i++)
//            {
//                var individual = CreateRandomIndividual();
//                if (individual != null)
//                    population.Add(individual);
//            }
//            if (population.Count == 0)
//                return (false, null);

//            for (int gen = 0; gen < maxGenerations; gen++)
//            {
//                population = population.OrderBy(ind => Fitness(ind)).ToList();
//                var best = population[0];
//                if (Fitness(best) == 0)
//                {
//                    return (true, best);
//                }
//                var nextGen = new List<List<TaskUnit>>
//                {
//                    population[0], population[1]
//                };
//                while (nextGen.Count < populationSize)
//                {
//                    var parent1 = TournamentSelection(population);
//                    var parent2 = TournamentSelection(population);
//                    var (child1, child2) = Crossover(parent1, parent2);
//                    Mutate(child1);
//                    Mutate(child2);
//                    nextGen.Add(child1);
//                    if (nextGen.Count < populationSize)
//                        nextGen.Add(child2);
//                }
//                population = nextGen;
//            }
//            population = population.OrderBy(ind => Fitness(ind)).ToList();
//            if (Fitness(population[0]) == 0)
//                return (true, population[0]);
//            return (false, null);
//        }

//        #endregion

//        #region Timetable Generation Endpoint

//        [HttpPost("generateCrossDepartmentTimetableBacktracking")]
//        public async Task<IActionResult> GenerateTimetable([FromBody] TimetableRequest request)
//        {
//            var cs = _configuration.GetConnectionString("DefaultConnection");
//            await using var conn = new NpgsqlConnection(cs);
//            await conn.OpenAsync();

//            string[] DAYS = new[] { "Mon", "Tue", "Wed", "Thu", "Fri" };
//            int HOURS = Math.Max(1, request.TotalHoursPerDay);

//            var timetableGrid = DAYS.ToDictionary(d => d, d => Enumerable.Range(1, HOURS).ToDictionary(h => h, _ => "---"));
//            var staffOcc = new Dictionary<string, Dictionary<string, HashSet<int>>>();
//            var labOcc = new Dictionary<string, Dictionary<string, HashSet<int>>>();

//            void EnsureDayMap<TKey>(Dictionary<TKey, Dictionary<string, HashSet<int>>> map, TKey key)
//            {
//                if (!map.ContainsKey(key))
//                    map[key] = DAYS.ToDictionary(d => d, _ => new HashSet<int>());
//            }

//            // Load once DB staff occupancy
//            await using (var cmd = new NpgsqlCommand("SELECT staff_code, day, hour FROM classtimetable", conn))
//            await using (var rd = await cmd.ExecuteReaderAsync())
//            {
//                while (await rd.ReadAsync())
//                {
//                    var sc = rd["staff_code"] as string ?? "---";
//                    var day = rd["day"] as string ?? "Mon";
//                    var hr = (int)rd["hour"];
//                    EnsureDayMap(staffOcc, sc);
//                    staffOcc[sc][day].Add(hr);
//                }
//            }

//            // Load once DB lab occupancy
//            await using (var cmd = new NpgsqlCommand("SELECT lab_id, day, hour FROM labtimetable", conn))
//            await using (var rd = await cmd.ExecuteReaderAsync())
//            {
//                while (await rd.ReadAsync())
//                {
//                    var lab = rd["lab_id"] as string ?? "---";
//                    var day = rd["day"] as string ?? "Mon";
//                    var hr = (int)rd["hour"];
//                    EnsureDayMap(labOcc, lab);
//                    labOcc[lab][day].Add(hr);
//                }
//            }

//            var subjects = request.Subjects?.Where(s => !string.IsNullOrWhiteSpace(s.StaffAssigned)).ToList();
//            if (subjects == null || subjects.Count == 0)
//                return BadRequest(new { message = "❌ No valid subjects with assigned staff found." });

//            var tasks = new List<TaskUnit>();
//            foreach (var s in subjects)
//            {
//                var type = (s.SubjectType ?? "theory").Trim().ToLowerInvariant();
//                switch (type)
//                {
//                    case "lab":
//                        tasks.Add(new TaskUnit
//                        {
//                            SubjectCode = s.SubjectCode ?? "---",
//                            SubjectName = s.SubjectName ?? "---",
//                            StaffAssigned = s.StaffAssigned,
//                            LabId = s.LabId,
//                            IsLab = true,
//                            Duration = 4,
//                            Kind = "LAB4"
//                        });
//                        break;
//                    case "embedded":
//                        tasks.Add(new TaskUnit
//                        {
//                            SubjectCode = s.SubjectCode ?? "---",
//                            SubjectName = s.SubjectName ?? "---",
//                            StaffAssigned = s.StaffAssigned,
//                            LabId = s.LabId,
//                            IsLab = true,
//                            Duration = 2,
//                            Kind = "EMB_LAB2"
//                        });
//                        tasks.Add(new TaskUnit
//                        {
//                            SubjectCode = s.SubjectCode ?? "---",
//                            SubjectName = s.SubjectName ?? "---",
//                            StaffAssigned = s.StaffAssigned,
//                            IsLab = false,
//                            Duration = 1,
//                            Kind = "EMB_TH1"
//                        });
//                        tasks.Add(new TaskUnit
//                        {
//                            SubjectCode = s.SubjectCode ?? "---",
//                            SubjectName = s.SubjectName ?? "---",
//                            StaffAssigned = s.StaffAssigned,
//                            IsLab = false,
//                            Duration = 1,
//                            Kind = "EMB_TH1"
//                        });
//                        break;
//                    default:
//                        int count = Math.Max(0, s.Credit);
//                        for (int i = 0; i < count; i++)
//                        {
//                            tasks.Add(new TaskUnit
//                            {
//                                SubjectCode = s.SubjectCode ?? "---",
//                                SubjectName = s.SubjectName ?? "---",
//                                StaffAssigned = s.StaffAssigned,
//                                IsLab = false,
//                                Duration = 1,
//                                Kind = "TH1"
//                            });
//                        }
//                        break;
//                }
//            }

//            Shuffle(tasks);

//            foreach (var t in tasks)
//            {
//                t.Domain.Clear();
//                foreach (var day in DAYS)
//                {
//                    for (int start = 1; start <= HOURS - t.Duration + 1; start++)
//                    {
//                        if (IsFreeAndNoConflict(t, day, start, staffOcc, labOcc, timetableGrid, HOURS))
//                            t.Domain.Add((day, start));
//                    }
//                }
//                if (t.Domain.Count == 0)
//                    return Ok(new { message = $"❌ No initial available slot for task {t.SubjectCode}.", receivedPayload = request });
//                Shuffle(t.Domain);
//            }

//            // Build conflict graph
//            BuildConflictGraph(tasks);

//            int recursionCounter = 0;

//            async Task<bool> RecursiveBacktracking(List<TaskUnit> currentTasks)
//            {
//                recursionCounter++;
//                if (recursionCounter > MAX_RECURSION_DEPTH) return false;

//                if (currentTasks.All(t => t.IsPlaced)) return true;

//                var task = currentTasks.Where(t => !t.IsPlaced).OrderBy(t => t.Domain.Count).FirstOrDefault();
//                if (task == null) return true;

//                var domainCopy = task.Domain.OrderBy(_ => rng.Next()).ToList();

//                foreach (var slot in domainCopy)
//                {
//                    if (!IsFreeAndNoConflict(task, slot.day, slot.start, staffOcc, labOcc, timetableGrid, HOURS))
//                        continue;

//                    AssignTask(task, slot, staffOcc, labOcc, timetableGrid);

//                    if (PropagateConstraints(currentTasks, task, slot))
//                    {
//                        if (await RecursiveBacktracking(currentTasks)) return true;
//                    }

//                    UnassignTask(task, staffOcc, labOcc, timetableGrid);

//                    var conflictComponent = GetConflictComponent(task);
//                    if (conflictComponent.Count == 0 || recursionCounter > MAX_RECURSION_DEPTH)
//                        return false;

//                    foreach (var t in conflictComponent)
//                    {
//                        UnassignTask(t, staffOcc, labOcc, timetableGrid);
//                        t.IsPlaced = false;
//                        t.Domain.Clear();
//                        foreach (var d in DAYS)
//                        {
//                            for (int st = 1; st <= HOURS - t.Duration + 1; st++)
//                            {
//                                if (IsFreeAndNoConflict(t, d, st, staffOcc, labOcc, timetableGrid, HOURS))
//                                    t.Domain.Add((d, st));
//                            }
//                        }
//                        if (t.Domain.Count == 0) return false;
//                    }

//                    var gaResult = await RunGeneticAlgorithmAsync(conflictComponent, DAYS, HOURS, staffOcc, labOcc);
//                    if (gaResult.Succeeded)
//                    {
//                        foreach (var t in conflictComponent)
//                        {
//                            t.Domain.Clear();
//                            t.Domain.Add((t.Day, t.StartHour));
//                            AssignTask(t, (t.Day, t.StartHour), staffOcc, labOcc, timetableGrid);
//                        }
//                        BuildConflictGraph(tasks);
//                        if (await RecursiveBacktracking(tasks)) return true;
//                        foreach (var t in conflictComponent)
//                            UnassignTask(t, staffOcc, labOcc, timetableGrid);
//                    }
//                    else
//                    {
//                        foreach (var t in conflictComponent)
//                            UnassignTask(t, staffOcc, labOcc, timetableGrid);
//                    }
//                }

//                return false;
//            }

//            var solved = await RecursiveBacktracking(tasks);

//            if (!solved)
//                return Ok(new { message = "❌ Unsolvable conflict detected", receivedPayload = request });

//            if (!ValidateFinalTimetable(tasks, HOURS))
//                return Ok(new { message = "❌ Timetable validation failed after generation.", receivedPayload = request });

//            await using var transaction = await conn.BeginTransactionAsync();

//            await using (var delClass = new NpgsqlCommand(@"
//                DELETE FROM classtimetable WHERE department_id=@department AND year=@year AND semester=@sem AND section=@section;", conn, transaction))
//            {
//                delClass.Parameters.AddWithValue("department", request.Department ?? "---");
//                delClass.Parameters.AddWithValue("year", request.Year ?? "---");
//                delClass.Parameters.AddWithValue("sem", request.Semester ?? "---");
//                delClass.Parameters.AddWithValue("section", request.Section ?? "---");
//                await delClass.ExecuteNonQueryAsync();
//            }

//            await using (var delLab = new NpgsqlCommand(@"
//                DELETE FROM labtimetable WHERE department=@department AND year=@year AND semester=@sem AND section=@section;", conn, transaction))
//            {
//                delLab.Parameters.AddWithValue("department", request.Department ?? "---");
//                delLab.Parameters.AddWithValue("year", request.Year ?? "---");
//                delLab.Parameters.AddWithValue("sem", request.Semester ?? "---");
//                delLab.Parameters.AddWithValue("section", request.Section ?? "---");
//                await delLab.ExecuteNonQueryAsync();
//            }

//            foreach (var t in tasks)
//            {
//                var (staffName, staffCode) = SplitStaff(t.StaffAssigned);
//                for (int h = t.StartHour; h < t.StartHour + t.Duration; h++)
//                {
//                    await using var icClass = new NpgsqlCommand(@"
//                        INSERT INTO classtimetable
//                        (staff_name, staff_code, department_id, year, semester, section, day, hour, subject_code, subject_name)
//                        VALUES
//                        (@staff_name, @staff_code, @department, @year, @semester, @section, @day, @hour, @subject_code, @subject_name);", conn, transaction);
//                    icClass.Parameters.AddWithValue("@staff_name", staffName ?? "---");
//                    icClass.Parameters.AddWithValue("@staff_code", staffCode ?? "---");
//                    icClass.Parameters.AddWithValue("@department", request.Department ?? "---");
//                    icClass.Parameters.AddWithValue("@year", request.Year ?? "---");
//                    icClass.Parameters.AddWithValue("@semester", request.Semester ?? "---");
//                    icClass.Parameters.AddWithValue("@section", request.Section ?? "---");
//                    icClass.Parameters.AddWithValue("@day", t.Day ?? "---");
//                    icClass.Parameters.AddWithValue("@hour", h);
//                    icClass.Parameters.AddWithValue("@subject_code", t.SubjectCode ?? "---");
//                    icClass.Parameters.AddWithValue("@subject_name", t.SubjectName ?? "---");
//                    await icClass.ExecuteNonQueryAsync();
//                }

//                if (t.IsLab && !string.IsNullOrEmpty(t.LabId))
//                {
//                    int labStart = t.StartHour == 4 ? 5 : t.StartHour;
//                    for (int h = labStart; h < t.StartHour + t.Duration; h++)
//                    {
//                        await using var icLab = new NpgsqlCommand(@"
//                            INSERT INTO labtimetable
//                            (lab_id, subject_code, subject_name, staff_name, department, year, semester, section, day, hour)
//                            VALUES
//                            (@lab_id, @subject_code, @subject_name, @staff_name, @department, @year, @semester, @section, @day, @hour);", conn, transaction);
//                        icLab.Parameters.AddWithValue("@lab_id", t.LabId ?? (object)DBNull.Value);
//                        icLab.Parameters.AddWithValue("@subject_code", t.SubjectCode ?? "---");
//                        icLab.Parameters.AddWithValue("@subject_name", t.SubjectName ?? "---");
//                        icLab.Parameters.AddWithValue("@staff_name", staffName ?? "---");
//                        icLab.Parameters.AddWithValue("@department", request.Department ?? "---");
//                        icLab.Parameters.AddWithValue("@year", request.Year ?? "---");
//                        icLab.Parameters.AddWithValue("@semester", request.Semester ?? "---");
//                        icLab.Parameters.AddWithValue("@section", request.Section ?? "---");
//                        icLab.Parameters.AddWithValue("@day", t.Day ?? "---");
//                        icLab.Parameters.AddWithValue("@hour", h);
//                        await icLab.ExecuteNonQueryAsync();
//                    }
//                }
//            }

//            await transaction.CommitAsync();

//            return Ok(new
//            {
//                message = "✅ Conflict-free timetable generated successfully.",
//                timetable = timetableGrid.Select(t => new { Day = t.Key, Slots = t.Value }),
//                usedLabIds = tasks.Where(t => t.IsLab && !string.IsNullOrEmpty(t.LabId)).Select(t => t.LabId).Distinct(),
//                receivedPayload = request
//            });
//        }

//        private bool ValidateFinalTimetable(List<TaskUnit> tasks, int totalHours)
//        {
//            var staffSchedule = new Dictionary<string, Dictionary<string, HashSet<int>>>();
//            var labSchedule = new Dictionary<string, Dictionary<string, HashSet<int>>>();

//            foreach (var t in tasks)
//            {
//                var (_, staffCode) = SplitStaff(t.StaffAssigned);

//                if (!staffSchedule.ContainsKey(staffCode))
//                    staffSchedule[staffCode] = new Dictionary<string, HashSet<int>>();
//                if (!staffSchedule[staffCode].ContainsKey(t.Day))
//                    staffSchedule[staffCode][t.Day] = new HashSet<int>();

//                if (t.IsLab && !string.IsNullOrEmpty(t.LabId))
//                {
//                    if (!labSchedule.ContainsKey(t.LabId))
//                        labSchedule[t.LabId] = new Dictionary<string, HashSet<int>>();
//                    if (!labSchedule[t.LabId].ContainsKey(t.Day))
//                        labSchedule[t.LabId][t.Day] = new HashSet<int>();
//                }

//                for (int h = t.StartHour; h < t.StartHour + t.Duration; h++)
//                {
//                    if (h < 1 || h > totalHours)
//                        return false;

//                    if (staffSchedule[staffCode][t.Day].Contains(h))
//                        return false;
//                    staffSchedule[staffCode][t.Day].Add(h);

//                    if (t.IsLab && !string.IsNullOrEmpty(t.LabId))
//                    {
//                        if (labSchedule[t.LabId][t.Day].Contains(h))
//                            return false;
//                        labSchedule[t.LabId][t.Day].Add(h);
//                    }

//                    if (t.IsLab && t.Duration == 4 && !(t.StartHour == 1 || t.StartHour == 4))
//                        return false;
//                }
//            }

//            var globalSlotMap = new Dictionary<(string Day, int Hour), TaskUnit>();
//            foreach (var t in tasks)
//            {
//                for (int h = t.StartHour; h < t.StartHour + t.Duration; h++)
//                {
//                    var key = (t.Day, h);
//                    if (globalSlotMap.ContainsKey(key))
//                        return false;
//                    globalSlotMap[key] = t;
//                }
//            }

//            return true;
//        }

//        #endregion


//    }
//}



//not so good after sih
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
//        private static readonly Random rng = new Random();
//        private const int MAX_RECURSION_DEPTH = 900;
//        private const int MAX_GENETIC_COMPONENT_SIZE = 10;

//        public TimetableController(IConfiguration configuration)
//        {
//            _configuration = configuration;
//        }

//        #region DTOs

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
//            public bool IsPlaced = false;
//            public List<(string day, int start)> Domain = new();
//            public List<TaskUnit> Conflicts = new();
//        }

//        #endregion

//        #region Helper Methods

//        private void Shuffle<T>(IList<T> list)
//        {
//            int n = list.Count;
//            while (n > 1)
//            {
//                n--;
//                int k = rng.Next(n + 1);
//                (list[k], list[n]) = (list[n], list[k]);
//            }
//        }

//        private (string staffName, string staffCode) SplitStaff(string staffAssigned)
//        {
//            if (string.IsNullOrWhiteSpace(staffAssigned))
//                return ("---", "---");
//            var name = staffAssigned;
//            var code = staffAssigned;
//            if (staffAssigned.Contains("("))
//            {
//                var parts = staffAssigned.Split('(', StringSplitOptions.TrimEntries);
//                name = parts[0].Trim();
//                code = parts.Length > 1 ? parts[1].Replace(")", "").Trim() : "---";
//            }

//            if (string.IsNullOrEmpty(code))
//                code = "---";

//            return (name, code);
//        }

//        private bool IsFreeAndNoConflict(TaskUnit t, string day, int start,
//            Dictionary<string, Dictionary<string, HashSet<int>>> staffOcc,
//            Dictionary<string, Dictionary<string, HashSet<int>>> labOcc,
//            Dictionary<string, Dictionary<int, string>> timetableGrid,
//            int totalHours)
//        {
//            for (int h = start; h < start + t.Duration; h++)
//            {
//                if (h < 1 || h > totalHours) return false;
//                if (timetableGrid[day][h] != "---") return false;
//            }
//            var (_, staffCode) = SplitStaff(t.StaffAssigned);
//            for (int h = start; h < start + t.Duration; h++)
//            {
//                if (staffOcc.TryGetValue(staffCode, out var dm) && dm.TryGetValue(day, out var set) && set.Contains(h))
//                    return false;
//            }
//            if (t.IsLab && !string.IsNullOrEmpty(t.LabId))
//            {
//                for (int h = start; h < start + t.Duration; h++)
//                {
//                    if (labOcc.TryGetValue(t.LabId, out var dm) && dm.TryGetValue(day, out var set) && set.Contains(h))
//                        return false;
//                }
//                if (t.Kind == "LAB4" && !(start == 1 || start == 4))
//                    return false;
//            }
//            return true;
//        }

//        private void AssignTask(TaskUnit t, (string day, int start) slot,
//            Dictionary<string, Dictionary<string, HashSet<int>>> staffOcc,
//            Dictionary<string, Dictionary<string, HashSet<int>>> labOcc,
//            Dictionary<string, Dictionary<int, string>> timetableGrid)
//        {
//            var (day, start) = slot;
//            var (_, staffCode) = SplitStaff(t.StaffAssigned);
//            if (day == null || staffCode == null)
//            {
//                Console.WriteLine($"Error: Attempted to assign with null key. day: {day}, staffCode: {staffCode}");
//                return;
//            }
//            for (int h = start; h < start + t.Duration; h++)
//            {
//                if (!timetableGrid.ContainsKey(day) || timetableGrid[day] == null)
//                {
//                    Console.WriteLine($"Error: Timetable grid missing for day: {day}");
//                    continue;
//                }
//                timetableGrid[day][h] = $"{t.SubjectCode} ({t.StaffAssigned})";
//                if (!staffOcc.ContainsKey(staffCode)) staffOcc[staffCode] = new Dictionary<string, HashSet<int>>();
//                if (!staffOcc[staffCode].ContainsKey(day)) staffOcc[staffCode][day] = new HashSet<int>();
//                staffOcc[staffCode][day].Add(h);
//                if (t.IsLab && !string.IsNullOrEmpty(t.LabId))
//                {
//                    if (!labOcc.ContainsKey(t.LabId)) labOcc[t.LabId] = new Dictionary<string, HashSet<int>>();
//                    if (!labOcc[t.LabId].ContainsKey(day)) labOcc[t.LabId][day] = new HashSet<int>();
//                    labOcc[t.LabId][day].Add(h);
//                }
//            }
//            t.Day = day;
//            t.StartHour = start;
//            t.IsPlaced = true;
//        }

//        private void UnassignTask(TaskUnit t,
//            Dictionary<string, Dictionary<string, HashSet<int>>> staffOcc,
//            Dictionary<string, Dictionary<string, HashSet<int>>> labOcc,
//            Dictionary<string, Dictionary<int, string>> timetableGrid)
//        {
//            if (!t.IsPlaced) return;
//            var day = t.Day;
//            var start = t.StartHour;
//            var (_, staffCode) = SplitStaff(t.StaffAssigned);
//            for (int h = start; h < start + t.Duration; h++)
//            {
//                timetableGrid[day][h] = "---";
//                staffOcc[staffCode][day].Remove(h);
//                if (t.IsLab && !string.IsNullOrEmpty(t.LabId))
//                    labOcc[t.LabId][day].Remove(h);
//            }
//            t.IsPlaced = false;
//            t.Day = null;
//            t.StartHour = 0;
//        }

//        private bool PropagateConstraints(List<TaskUnit> tasksList, TaskUnit assignedTask, (string day, int start) assignedSlot)
//        {
//            var snapshotDomains = tasksList.ToDictionary(t => t, t => t.Domain.ToList());
//            assignedTask.Domain = new List<(string, int)> { assignedSlot };

//            var queue = new Queue<TaskUnit>();
//            foreach (var t in tasksList)
//                if (t != assignedTask && !t.IsPlaced)
//                    queue.Enqueue(t);

//            while (queue.Count > 0)
//            {
//                var other = queue.Dequeue();
//                if (other.IsPlaced || other == assignedTask) continue;
//                var filtered = new List<(string day, int start)>();
//                foreach (var pos in other.Domain)
//                {
//                    bool conflict = false;
//                    if (pos.day == assignedSlot.day)
//                    {
//                        int start1 = pos.start;
//                        int end1 = pos.start + other.Duration - 1;
//                        int start2 = assignedSlot.start;
//                        int end2 = assignedSlot.start + assignedTask.Duration - 1;
//                        bool overlap = end1 >= start2 && end2 >= start1;
//                        if (overlap)
//                        {
//                            var (_, staff1) = SplitStaff(other.StaffAssigned);
//                            var (_, staff2) = SplitStaff(assignedTask.StaffAssigned);
//                            if (staff1 == staff2) conflict = true;

//                            if (!conflict && other.IsLab && assignedTask.IsLab &&
//                                !string.IsNullOrEmpty(other.LabId) && !string.IsNullOrEmpty(assignedTask.LabId) && other.LabId == assignedTask.LabId)
//                                conflict = true;
//                        }
//                    }
//                    if (!conflict) filtered.Add(pos);
//                }
//                if (filtered.Count != other.Domain.Count)
//                {
//                    other.Domain = filtered;
//                    if (other.Domain.Count == 0)
//                    {
//                        foreach (var kvp in snapshotDomains) kvp.Key.Domain = kvp.Value;
//                        return false;
//                    }
//                    foreach (var t in other.Conflicts)
//                    {
//                        if (!t.IsPlaced && t != assignedTask && !queue.Contains(t))
//                        {
//                            queue.Enqueue(t);
//                        }
//                    }
//                }
//            }

//            return true;
//        }

//        private void BuildConflictGraph(List<TaskUnit> tasks)
//        {
//            foreach (var t in tasks) t.Conflicts.Clear();

//            for (int i = 0; i < tasks.Count; i++)
//            {
//                for (int j = i + 1; j < tasks.Count; j++)
//                {
//                    var a = tasks[i];
//                    var b = tasks[j];
//                    if (HaveConflict(a, b))
//                    {
//                        a.Conflicts.Add(b);
//                        b.Conflicts.Add(a);
//                    }
//                }
//            }
//        }

//        private bool HaveConflict(TaskUnit a, TaskUnit b)
//        {
//            var (_, staffA) = SplitStaff(a.StaffAssigned);
//            var (_, staffB) = SplitStaff(b.StaffAssigned);

//            if (staffA == staffB) return true;

//            if (a.IsLab && b.IsLab && !string.IsNullOrEmpty(a.LabId) && !string.IsNullOrEmpty(b.LabId) && a.LabId == b.LabId)
//                return true;

//            foreach (var posA in a.Domain)
//                foreach (var posB in b.Domain)
//                    if (posA.day == posB.day)
//                    {
//                        int startA = posA.start;
//                        int endA = startA + a.Duration - 1;
//                        int startB = posB.start;
//                        int endB = startB + b.Duration - 1;
//                        if (endA >= startB && endB >= startA) return true;
//                    }

//            return false;
//        }

//        private List<TaskUnit> GetConflictComponent(TaskUnit task)
//        {
//            var visited = new HashSet<TaskUnit>();
//            var stack = new Stack<TaskUnit>();
//            var component = new List<TaskUnit>();

//            stack.Push(task);

//            while (stack.Count > 0)
//            {
//                var current = stack.Pop();
//                if (visited.Contains(current)) continue;
//                visited.Add(current);
//                component.Add(current);

//                foreach (var neighbor in current.Conflicts)
//                    if (!visited.Contains(neighbor))
//                        stack.Push(neighbor);
//            }

//            return component;
//        }

//        // Order tasks by MRV + degree heuristic and priority of lab > embedded lab > theory
//        private List<TaskUnit> OrderTasks(List<TaskUnit> tasks)
//        {
//            return tasks.OrderBy(t =>
//            {
//                int typePriority = t.Kind switch
//                {
//                    "LAB4" => 0,
//                    "EMB_LAB2" => 1,
//                    "EMB_TH1" => 2,
//                    "TH1" => 3,
//                    _ => 4
//                };
//                return typePriority;
//            })
//            .ThenBy(t => t.Domain.Count) // MRV
//            .ThenByDescending(t => t.Conflicts.Count) // Degree heuristic
//            .ToList();
//        }

//        // Order domain slots by Least Constraining Value heuristic
//        private List<(string day, int start)> OrderDomainByLCV(TaskUnit task, Dictionary<string, Dictionary<string, HashSet<int>>> staffOcc,
//            Dictionary<string, Dictionary<string, HashSet<int>>> labOcc,
//            Dictionary<string, Dictionary<int, string>> timetableGrid,
//            List<TaskUnit> tasks, int totalHours)
//        {
//            var counts = new Dictionary<(string day, int start), int>();

//            foreach (var slot in task.Domain)
//            {
//                int eliminationCount = 0;
//                foreach (var other in tasks)
//                {
//                    if (other == task || other.IsPlaced) continue;
//                    foreach (var otherSlot in other.Domain)
//                    {
//                        if (slot.day == otherSlot.day)
//                        {
//                            int startA = slot.start;
//                            int endA = slot.start + task.Duration - 1;
//                            int startB = otherSlot.start;
//                            int endB = otherSlot.start + other.Duration - 1;
//                            bool overlap = endA >= startB && endB >= startA;
//                            if (overlap)
//                            {
//                                var (_, staffA) = SplitStaff(task.StaffAssigned);
//                                var (_, staffB) = SplitStaff(other.StaffAssigned);
//                                if (staffA == staffB) eliminationCount++;
//                                else if (task.IsLab && other.IsLab && !string.IsNullOrEmpty(task.LabId) && !string.IsNullOrEmpty(other.LabId) && task.LabId == other.LabId)
//                                    eliminationCount++;
//                            }
//                        }
//                    }
//                }

//                counts[slot] = eliminationCount;
//            }

//            // Least constraining = minimize elimination count
//            return counts.OrderBy(kv => kv.Value).Select(kv => kv.Key).ToList();
//        }

//        #endregion

//        #region Genetic Algorithm Fallback

//        private async Task<(bool Succeeded, List<TaskUnit> Result)> RunGeneticAlgorithmAsync(
//            List<TaskUnit> tasksToAssign,
//            string[] days,
//            int hours,
//            Dictionary<string, Dictionary<string, HashSet<int>>> staffOcc,
//            Dictionary<string, Dictionary<string, HashSet<int>>> labOcc)
//        {
//            const int populationSize = 50;
//            const int maxGenerations = 150;
//            const double mutationRate = 0.15;

//            bool CanPlace(TaskUnit t, string day, int start)
//            {
//                if (start < 1 || start + t.Duration - 1 > hours)
//                    return false;

//                var (_, staffCode) = SplitStaff(t.StaffAssigned);
//                for (int h = start; h < start + t.Duration; h++)
//                {
//                    if (staffOcc.TryGetValue(staffCode, out var dayMap) && dayMap.TryGetValue(day, out var staffHours) && staffHours.Contains(h))
//                        return false;
//                    if (t.IsLab && !string.IsNullOrEmpty(t.LabId))
//                    {
//                        if (labOcc.TryGetValue(t.LabId, out var labDayMap) && labDayMap.TryGetValue(day, out var labHours) && labHours.Contains(h))
//                            return false;
//                    }
//                }
//                if (t.IsLab && t.Duration == 4 && !(start == 1 || start == 4))
//                    return false;

//                return true;
//            }

//            List<TaskUnit> CreateRandomIndividual()
//            {
//                var individual = new List<TaskUnit>();
//                foreach (var t in tasksToAssign)
//                {
//                    var validSlots = new List<(string day, int start)>();
//                    foreach (var day in days)
//                    {
//                        for (int start = 1; start <= hours - t.Duration + 1; start++)
//                        {
//                            if (CanPlace(t, day, start))
//                                validSlots.Add((day, start));
//                        }
//                    }
//                    if (validSlots.Count == 0)
//                        return null;
//                    var chosen = validSlots[rng.Next(validSlots.Count)];
//                    var copy = new TaskUnit
//                    {
//                        SubjectCode = t.SubjectCode,
//                        StaffAssigned = t.StaffAssigned,
//                        LabId = t.LabId,
//                        IsLab = t.IsLab,
//                        Duration = t.Duration,
//                        Day = chosen.day,
//                        StartHour = chosen.start,
//                        IsPlaced = true
//                    };
//                    individual.Add(copy);
//                }
//                return individual;
//            }

//            int Fitness(List<TaskUnit> individual)
//            {
//                int penalty = 0;
//                var staffSchedule = new Dictionary<string, Dictionary<string, HashSet<int>>>();
//                var labSchedule = new Dictionary<string, Dictionary<string, HashSet<int>>>();

//                foreach (var t in tasksToAssign)
//                {
//                    var (_, staffCode) = SplitStaff(t.StaffAssigned);
//                    if (!staffSchedule.ContainsKey(staffCode))
//                        staffSchedule[staffCode] = days.ToDictionary(d => d, _ => new HashSet<int>());
//                    if (t.IsLab && !string.IsNullOrEmpty(t.LabId) && !labSchedule.ContainsKey(t.LabId))
//                        labSchedule[t.LabId] = days.ToDictionary(d => d, _ => new HashSet<int>());
//                }

//                foreach (var t in individual)
//                {
//                    var (_, staffCode) = SplitStaff(t.StaffAssigned);
//                    for (int h = t.StartHour; h < t.StartHour + t.Duration; h++)
//                    {
//                        if (staffOcc.TryGetValue(staffCode, out var dayMap) && dayMap.TryGetValue(t.Day, out var existingHours) && existingHours.Contains(h))
//                            penalty += 10;

//                        if (t.IsLab && !string.IsNullOrEmpty(t.LabId) && labOcc.TryGetValue(t.LabId, out var labDayMap) && labDayMap.TryGetValue(t.Day, out var labExisting) && labExisting.Contains(h))
//                            penalty += 10;

//                        if (staffSchedule[staffCode][t.Day].Contains(h))
//                            penalty += 5;
//                        else
//                            staffSchedule[staffCode][t.Day].Add(h);

//                        if (t.IsLab && !string.IsNullOrEmpty(t.LabId))
//                        {
//                            if (labSchedule[t.LabId][t.Day].Contains(h))
//                                penalty += 5;
//                            else
//                                labSchedule[t.LabId][t.Day].Add(h);
//                        }
//                    }

//                    if (t.IsLab && t.Duration == 4 && !(t.StartHour == 1 || t.StartHour == 4))
//                        penalty += 20;
//                }
//                return penalty;
//            }

//            List<TaskUnit> TournamentSelection(List<List<TaskUnit>> population)
//            {
//                int k = 3;
//                var selected = new List<List<TaskUnit>>();
//                for (int i = 0; i < k; i++)
//                    selected.Add(population[rng.Next(population.Count)]);
//                return selected.OrderBy(ind => Fitness(ind)).First();
//            }

//            (List<TaskUnit>, List<TaskUnit>) Crossover(List<TaskUnit> parent1, List<TaskUnit> parent2)
//            {
//                int point = rng.Next(1, parent1.Count);
//                var child1 = new List<TaskUnit>();
//                var child2 = new List<TaskUnit>();
//                for (int i = 0; i < parent1.Count; i++)
//                {
//                    child1.Add(i < point ? parent1[i] : parent2[i]);
//                    child2.Add(i < point ? parent2[i] : parent1[i]);
//                }
//                return (child1, child2);
//            }

//            void Mutate(List<TaskUnit> individual)
//            {
//                for (int i = 0; i < individual.Count; i++)
//                {
//                    if (rng.NextDouble() < mutationRate)
//                    {
//                        var t = tasksToAssign[i];
//                        var validSlots = new List<(string day, int start)>();
//                        foreach (var day in days)
//                        {
//                            for (int start = 1; start <= hours - t.Duration + 1; start++)
//                            {
//                                if (CanPlace(t, day, start))
//                                    validSlots.Add((day, start));
//                            }
//                        }
//                        if (validSlots.Count > 0)
//                        {
//                            var chosen = validSlots[rng.Next(validSlots.Count)];
//                            individual[i].Day = chosen.day;
//                            individual[i].StartHour = chosen.start;
//                        }
//                    }
//                }
//            }

//            var population = new List<List<TaskUnit>>();
//            for (int i = 0; i < populationSize; i++)
//            {
//                var individual = CreateRandomIndividual();
//                if (individual != null)
//                    population.Add(individual);
//            }
//            if (population.Count == 0)
//                return (false, null);

//            for (int gen = 0; gen < maxGenerations; gen++)
//            {
//                population = population.OrderBy(ind => Fitness(ind)).ToList();
//                var best = population[0];
//                if (Fitness(best) == 0)
//                {
//                    return (true, best);
//                }
//                var nextGen = new List<List<TaskUnit>>
//                {
//                    population[0], population[1]
//                };
//                while (nextGen.Count < populationSize)
//                {
//                    var parent1 = TournamentSelection(population);
//                    var parent2 = TournamentSelection(population);
//                    var (child1, child2) = Crossover(parent1, parent2);
//                    Mutate(child1);
//                    Mutate(child2);
//                    nextGen.Add(child1);
//                    if (nextGen.Count < populationSize)
//                        nextGen.Add(child2);
//                }
//                population = nextGen;
//            }
//            population = population.OrderBy(ind => Fitness(ind)).ToList();
//            if (Fitness(population[0]) == 0)
//                return (true, population[0]);
//            return (false, null);
//        }

//        #endregion

//        #region Timetable Generation Endpoint

//        [HttpPost("generateCrossDepartmentTimetableBacktracking")]
//        public async Task<IActionResult> GenerateTimetable([FromBody] TimetableRequest request)
//        {
//            var cs = _configuration.GetConnectionString("DefaultConnection");
//            await using var conn = new NpgsqlConnection(cs);
//            await conn.OpenAsync();

//            string[] DAYS = new[] { "Mon", "Tue", "Wed", "Thu", "Fri" };
//            int HOURS = Math.Max(1, request.TotalHoursPerDay);

//            var timetableGrid = DAYS.ToDictionary(d => d, d => Enumerable.Range(1, HOURS).ToDictionary(h => h, _ => "---"));
//            var staffOcc = new Dictionary<string, Dictionary<string, HashSet<int>>>();
//            var labOcc = new Dictionary<string, Dictionary<string, HashSet<int>>>();

//            void EnsureDayMap<TKey>(Dictionary<TKey, Dictionary<string, HashSet<int>>> map, TKey key)
//            {
//                if (!map.ContainsKey(key))
//                    map[key] = DAYS.ToDictionary(d => d, _ => new HashSet<int>());
//            }

//            // Load once DB staff occupancy
//            await using (var cmd = new NpgsqlCommand("SELECT staff_code, day, hour FROM classtimetable", conn))
//            await using (var rd = await cmd.ExecuteReaderAsync())
//            {
//                while (await rd.ReadAsync())
//                {
//                    var sc = rd["staff_code"] as string ?? "---";
//                    var day = rd["day"] as string ?? "Mon";
//                    var hr = (int)rd["hour"];
//                    EnsureDayMap(staffOcc, sc);
//                    staffOcc[sc][day].Add(hr);
//                }
//            }

//            // Load once DB lab occupancy
//            await using (var cmd = new NpgsqlCommand("SELECT lab_id, day, hour FROM labtimetable", conn))
//            await using (var rd = await cmd.ExecuteReaderAsync())
//            {
//                while (await rd.ReadAsync())
//                {
//                    var lab = rd["lab_id"] as string ?? "---";
//                    var day = rd["day"] as string ?? "Mon";
//                    var hr = (int)rd["hour"];
//                    EnsureDayMap(labOcc, lab);
//                    labOcc[lab][day].Add(hr);
//                }
//            }

//            var subjects = request.Subjects?.Where(s => !string.IsNullOrWhiteSpace(s.StaffAssigned)).ToList();
//            if (subjects == null || subjects.Count == 0)
//                return BadRequest(new { message = "❌ No valid subjects with assigned staff found." });

//            var tasks = new List<TaskUnit>();
//            foreach (var s in subjects)
//            {
//                var type = (s.SubjectType ?? "theory").Trim().ToLowerInvariant();
//                switch (type)
//                {
//                    case "lab":
//                        tasks.Add(new TaskUnit
//                        {
//                            SubjectCode = s.SubjectCode ?? "---",
//                            SubjectName = s.SubjectName ?? "---",
//                            StaffAssigned = s.StaffAssigned,
//                            LabId = s.LabId,
//                            IsLab = true,
//                            Duration = 4,
//                            Kind = "LAB4"
//                        });
//                        break;
//                    case "embedded":
//                        tasks.Add(new TaskUnit
//                        {
//                            SubjectCode = s.SubjectCode ?? "---",
//                            SubjectName = s.SubjectName ?? "---",
//                            StaffAssigned = s.StaffAssigned,
//                            LabId = s.LabId,
//                            IsLab = true,
//                            Duration = 2,
//                            Kind = "EMB_LAB2"
//                        });
//                        tasks.Add(new TaskUnit
//                        {
//                            SubjectCode = s.SubjectCode ?? "---",
//                            SubjectName = s.SubjectName ?? "---",
//                            StaffAssigned = s.StaffAssigned,
//                            IsLab = false,
//                            Duration = 1,
//                            Kind = "EMB_TH1"
//                        });
//                        tasks.Add(new TaskUnit
//                        {
//                            SubjectCode = s.SubjectCode ?? "---",
//                            SubjectName = s.SubjectName ?? "---",
//                            StaffAssigned = s.StaffAssigned,
//                            IsLab = false,
//                            Duration = 1,
//                            Kind = "EMB_TH1"
//                        });
//                        break;
//                    default:
//                        int count = Math.Max(0, s.Credit);
//                        for (int i = 0; i < count; i++)
//                        {
//                            tasks.Add(new TaskUnit
//                            {
//                                SubjectCode = s.SubjectCode ?? "---",
//                                SubjectName = s.SubjectName ?? "---",
//                                StaffAssigned = s.StaffAssigned,
//                                IsLab = false,
//                                Duration = 1,
//                                Kind = "TH1"
//                            });
//                        }
//                        break;
//                }
//            }

//            Shuffle(tasks);

//            foreach (var t in tasks)
//            {
//                t.Domain.Clear();
//                foreach (var day in DAYS)
//                {
//                    for (int start = 1; start <= HOURS - t.Duration + 1; start++)
//                    {
//                        if (IsFreeAndNoConflict(t, day, start, staffOcc, labOcc, timetableGrid, HOURS))
//                            t.Domain.Add((day, start));
//                    }
//                }
//                if (t.Domain.Count == 0)
//                    return Ok(new { message = $"❌ No initial available slot for task {t.SubjectCode}.", receivedPayload = request });
//                Shuffle(t.Domain);
//            }

//            BuildConflictGraph(tasks);

//            int recursionCounter = 0;

//            async Task<bool> RecursiveBacktracking(List<TaskUnit> currentTasks)
//            {
//                recursionCounter++;
//                if (recursionCounter > MAX_RECURSION_DEPTH) return false;

//                if (currentTasks.All(t => t.IsPlaced)) return true;

//                var orderedTasks = OrderTasks(currentTasks.Where(t => !t.IsPlaced).ToList());

//                if (orderedTasks.Count == 0) return true;

//                var task = orderedTasks.First();

//                var domainByLCV = OrderDomainByLCV(task, staffOcc, labOcc, timetableGrid, currentTasks, HOURS);

//                foreach (var slot in domainByLCV)
//                {
//                    if (!IsFreeAndNoConflict(task, slot.day, slot.start, staffOcc, labOcc, timetableGrid, HOURS))
//                        continue;

//                    AssignTask(task, slot, staffOcc, labOcc, timetableGrid);

//                    if (!PropagateConstraints(currentTasks, task, slot))
//                    {
//                        UnassignTask(task, staffOcc, labOcc, timetableGrid);
//                        continue;
//                    }

//                    if (await RecursiveBacktracking(currentTasks)) return true;

//                    UnassignTask(task, staffOcc, labOcc, timetableGrid);

//                    var conflictComponent = GetConflictComponent(task);

//                    if (conflictComponent.Count == 0 || recursionCounter > MAX_RECURSION_DEPTH)
//                        return false;

//                    foreach (var t in conflictComponent)
//                    {
//                        UnassignTask(t, staffOcc, labOcc, timetableGrid);
//                        t.IsPlaced = false;
//                        t.Domain.Clear();
//                        foreach (var d in DAYS)
//                        {
//                            for (int st = 1; st <= HOURS - t.Duration + 1; st++)
//                            {
//                                if (IsFreeAndNoConflict(t, d, st, staffOcc, labOcc, timetableGrid, HOURS))
//                                    t.Domain.Add((d, st));
//                            }
//                        }
//                        if (t.Domain.Count == 0) return false;
//                    }

//                    // Chunk and run genetic algorithm fallback only for components smaller than max size
//                    if (conflictComponent.Count <= MAX_GENETIC_COMPONENT_SIZE)
//                    {
//                        var gaResult = await RunGeneticAlgorithmAsync(conflictComponent, DAYS, HOURS, staffOcc, labOcc);
//                        if (gaResult.Succeeded)
//                        {
//                            foreach (var t in conflictComponent)
//                            {
//                                t.Domain.Clear();
//                                t.Domain.Add((t.Day, t.StartHour));
//                                AssignTask(t, (t.Day, t.StartHour), staffOcc, labOcc, timetableGrid);
//                            }

//                            BuildConflictGraph(currentTasks);

//                            if (await RecursiveBacktracking(currentTasks))
//                                return true;

//                            foreach (var t in conflictComponent)
//                            {
//                                UnassignTask(t, staffOcc, labOcc, timetableGrid);
//                            }
//                        }

//                        else
//                        {
//                            foreach (var t in conflictComponent)
//                                UnassignTask(t, staffOcc, labOcc, timetableGrid);
//                        }
//                    }
//                    else
//                    {
//                        // For larger conflict components, skip genetic fallback to avoid complexity overhead here
//                        return false;
//                    }
//                }

//                return false;
//            }

//            var solved = await RecursiveBacktracking(tasks);

//            if (!solved)
//                return Ok(new { message = "❌ Unsolvable conflict detected", receivedPayload = request });

//            if (!ValidateFinalTimetable(tasks, HOURS))
//                return Ok(new { message = "❌ Timetable validation failed after generation.", receivedPayload = request });

//            await using var transaction = await conn.BeginTransactionAsync();

//            await using (var delClass = new NpgsqlCommand(@"
//                DELETE FROM classtimetable WHERE department_id=@department AND year=@year AND semester=@sem AND section=@section;", conn, transaction))
//            {
//                delClass.Parameters.AddWithValue("department", request.Department ?? "---");
//                delClass.Parameters.AddWithValue("year", request.Year ?? "---");
//                delClass.Parameters.AddWithValue("sem", request.Semester ?? "---");
//                delClass.Parameters.AddWithValue("section", request.Section ?? "---");
//                await delClass.ExecuteNonQueryAsync();
//            }

//            await using (var delLab = new NpgsqlCommand(@"
//                DELETE FROM labtimetable WHERE department=@department AND year=@year AND semester=@sem AND section=@section;", conn, transaction))
//            {
//                delLab.Parameters.AddWithValue("department", request.Department ?? "---");
//                delLab.Parameters.AddWithValue("year", request.Year ?? "---");
//                delLab.Parameters.AddWithValue("sem", request.Semester ?? "---");
//                delLab.Parameters.AddWithValue("section", request.Section ?? "---");
//                await delLab.ExecuteNonQueryAsync();
//            }

//            foreach (var t in tasks)
//            {
//                var (staffName, staffCode) = SplitStaff(t.StaffAssigned);
//                for (int h = t.StartHour; h < t.StartHour + t.Duration; h++)
//                {
//                    await using var icClass = new NpgsqlCommand(@"
//                        INSERT INTO classtimetable
//                        (staff_name, staff_code, department_id, year, semester, section, day, hour, subject_code, subject_name)
//                        VALUES
//                        (@staff_name, @staff_code, @department, @year, @semester, @section, @day, @hour, @subject_code, @subject_name);", conn, transaction);
//                    icClass.Parameters.AddWithValue("@staff_name", staffName ?? "---");
//                    icClass.Parameters.AddWithValue("@staff_code", staffCode ?? "---");
//                    icClass.Parameters.AddWithValue("@department", request.Department ?? "---");
//                    icClass.Parameters.AddWithValue("@year", request.Year ?? "---");
//                    icClass.Parameters.AddWithValue("@semester", request.Semester ?? "---");
//                    icClass.Parameters.AddWithValue("@section", request.Section ?? "---");
//                    icClass.Parameters.AddWithValue("@day", t.Day ?? "---");
//                    icClass.Parameters.AddWithValue("@hour", h);
//                    icClass.Parameters.AddWithValue("@subject_code", t.SubjectCode ?? "---");
//                    icClass.Parameters.AddWithValue("@subject_name", t.SubjectName ?? "---");
//                    await icClass.ExecuteNonQueryAsync();
//                }

//                if (t.IsLab && !string.IsNullOrEmpty(t.LabId))
//                {
//                    int labStart = t.StartHour == 4 ? 5 : t.StartHour;
//                    for (int h = labStart; h < t.StartHour + t.Duration; h++)
//                    {
//                        await using var icLab = new NpgsqlCommand(@"
//                            INSERT INTO labtimetable
//                            (lab_id, subject_code, subject_name, staff_name, department, year, semester, section, day, hour)
//                            VALUES
//                            (@lab_id, @subject_code, @subject_name, @staff_name, @department, @year, @semester, @section, @day, @hour);", conn, transaction);
//                        icLab.Parameters.AddWithValue("@lab_id", t.LabId ?? (object)DBNull.Value);
//                        icLab.Parameters.AddWithValue("@subject_code", t.SubjectCode ?? "---");
//                        icLab.Parameters.AddWithValue("@subject_name", t.SubjectName ?? "---");
//                        icLab.Parameters.AddWithValue("@staff_name", staffName ?? "---");
//                        icLab.Parameters.AddWithValue("@department", request.Department ?? "---");
//                        icLab.Parameters.AddWithValue("@year", request.Year ?? "---");
//                        icLab.Parameters.AddWithValue("@semester", request.Semester ?? "---");
//                        icLab.Parameters.AddWithValue("@section", request.Section ?? "---");
//                        icLab.Parameters.AddWithValue("@day", t.Day ?? "---");
//                        icLab.Parameters.AddWithValue("@hour", h);
//                        await icLab.ExecuteNonQueryAsync();
//                    }
//                }
//            }

//            await transaction.CommitAsync();

//            return Ok(new
//            {
//                message = "✅ Conflict-free timetable generated successfully.",
//                timetable = timetableGrid.Select(t => new { Day = t.Key, Slots = t.Value }),
//                usedLabIds = tasks.Where(t => t.IsLab && !string.IsNullOrEmpty(t.LabId)).Select(t => t.LabId).Distinct(),
//                receivedPayload = request
//            });
//        }

//        private bool ValidateFinalTimetable(List<TaskUnit> tasks, int totalHours)
//        {
//            var staffSchedule = new Dictionary<string, Dictionary<string, HashSet<int>>>();
//            var labSchedule = new Dictionary<string, Dictionary<string, HashSet<int>>>();

//            foreach (var t in tasks)
//            {
//                var (_, staffCode) = SplitStaff(t.StaffAssigned);

//                if (!staffSchedule.ContainsKey(staffCode))
//                    staffSchedule[staffCode] = new Dictionary<string, HashSet<int>>();
//                if (!staffSchedule[staffCode].ContainsKey(t.Day))
//                    staffSchedule[staffCode][t.Day] = new HashSet<int>();

//                if (t.IsLab && !string.IsNullOrEmpty(t.LabId))
//                {
//                    if (!labSchedule.ContainsKey(t.LabId))
//                        labSchedule[t.LabId] = new Dictionary<string, HashSet<int>>();
//                    if (!labSchedule[t.LabId].ContainsKey(t.Day))
//                        labSchedule[t.LabId][t.Day] = new HashSet<int>();
//                }

//                for (int h = t.StartHour; h < t.StartHour + t.Duration; h++)
//                {
//                    if (h < 1 || h > totalHours)
//                        return false;

//                    if (staffSchedule[staffCode][t.Day].Contains(h))
//                        return false;
//                    staffSchedule[staffCode][t.Day].Add(h);

//                    if (t.IsLab && !string.IsNullOrEmpty(t.LabId))
//                    {
//                        if (labSchedule[t.LabId][t.Day].Contains(h))
//                            return false;
//                        labSchedule[t.LabId][t.Day].Add(h);
//                    }

//                    if (t.IsLab && t.Duration == 4 && !(t.StartHour == 1 || t.StartHour == 4))
//                        return false;
//                }
//            }

//            var globalSlotMap = new Dictionary<(string Day, int Hour), TaskUnit>();
//            foreach (var t in tasks)
//            {
//                for (int h = t.StartHour; h < t.StartHour + t.Duration; h++)
//                {
//                    var key = (t.Day, h);
//                    if (globalSlotMap.ContainsKey(key))
//                        return false;
//                    globalSlotMap[key] = t;
//                }
//            }

//            return true;
//        }

//        #endregion

//    }
//}








//using Microsoft.AspNetCore.Mvc;
//using Microsoft.Extensions.Configuration;
//using Npgsql;
//using System;
//using System.Collections.Generic;
//using System.Linq;
//using System.Threading.Tasks;
//using System.Text;

//namespace Timetablegenerator.Controllers
//{
//    [ApiController]
//    [Route("api/[controller]")]
//    public class EnhancedTimetableController : ControllerBase
//    {
//        private readonly IConfiguration _configuration;
//        private static readonly Random rng = new Random();
//        private const int MAX_RECURSION_DEPTH = 900;
//        private const int MAX_GENETIC_COMPONENT_SIZE = 15;
//        private const int GA_MAX_GENERATIONS = 100;
//        private const int GA_POPULATION_SIZE = 50;
//        private const double GA_MUTATION_RATE = 0.15;

//        public EnhancedTimetableController(IConfiguration configuration)
//        {
//            _configuration = configuration;
//        }

//        #region DTOs and Data Structures

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
//            public string SubjectType { get; set; } // "theory", "lab", "embedded"
//            public int Credit { get; set; }
//            public string StaffAssigned { get; set; }
//            public string LabId { get; set; }
//        }

//        public class TaskUnit
//        {
//            public string SubjectCode { get; set; }
//            public string SubjectName { get; set; }
//            public string StaffAssigned { get; set; }
//            public string LabId { get; set; }
//            public bool IsLab { get; set; }
//            public int Duration { get; set; }
//            public string Kind { get; set; } // "LAB4", "EMB_LAB2", "EMB_TH1", "TH1"

//            // Assignment state
//            public string Day { get; set; }
//            public int StartHour { get; set; }
//            public bool IsPlaced { get; set; } = false;

//            // Constraint satisfaction properties
//            public List<(string day, int start)> DomainSlots { get; set; } = new();
//            public List<TaskUnit> Conflicts { get; set; } = new();
//            public (string day, int start) AssignedSlot => IsPlaced ? (Day, StartHour) : ("", 0);

//            public TaskUnit Clone()
//            {
//                return new TaskUnit
//                {
//                    SubjectCode = this.SubjectCode,
//                    SubjectName = this.SubjectName,
//                    StaffAssigned = this.StaffAssigned,
//                    LabId = this.LabId,
//                    IsLab = this.IsLab,
//                    Duration = this.Duration,
//                    Kind = this.Kind,
//                    Day = this.Day,
//                    StartHour = this.StartHour,
//                    IsPlaced = this.IsPlaced,
//                    DomainSlots = new List<(string, int)>(this.DomainSlots)
//                };
//            }
//        }

//        public class GAIndividual
//        {
//            public Dictionary<TaskUnit, (string day, int start)> Assignments { get; set; } = new();
//            public int Fitness { get; set; } = int.MaxValue;

//            public GAIndividual Clone()
//            {
//                return new GAIndividual
//                {
//                    Assignments = new Dictionary<TaskUnit, (string, int)>(this.Assignments),
//                    Fitness = this.Fitness
//                };
//            }
//        }

//        #endregion

//        #region Helper Methods

//        private void Shuffle<T>(IList<T> list)
//        {
//            int n = list.Count;
//            while (n > 1)
//            {
//                n--;
//                int k = rng.Next(n + 1);
//                (list[k], list[n]) = (list[n], list[k]);
//            }
//        }

//        private (string staffName, string staffCode) SplitStaff(string staffAssigned)
//        {
//            if (string.IsNullOrWhiteSpace(staffAssigned))
//                return ("---", "---");

//            var name = staffAssigned;
//            var code = staffAssigned;

//            if (staffAssigned.Contains("("))
//            {
//                var parts = staffAssigned.Split('(', StringSplitOptions.TrimEntries);
//                name = parts[0].Trim();
//                code = parts.Length > 1 ? parts[1].Replace(")", "").Trim() : "---";
//            }

//            if (string.IsNullOrEmpty(code))
//                code = "---";

//            return (name, code);
//        }

//        private bool IsFreeAndNoConflict(TaskUnit task, string day, int start,
//            Dictionary<string, Dictionary<string, HashSet<int>>> staffOcc,
//            Dictionary<string, Dictionary<string, HashSet<int>>> labOcc,
//            Dictionary<string, Dictionary<int, string>> timetableGrid,
//            int totalHours)
//        {
//            // Check time bounds
//            if (start < 1 || start + task.Duration - 1 > totalHours)
//                return false;

//            // Check grid availability
//            for (int h = start; h < start + task.Duration; h++)
//            {
//                if (timetableGrid[day][h] != "---")
//                    return false;
//            }

//            // Check staff conflicts
//            var (_, staffCode) = SplitStaff(task.StaffAssigned);
//            for (int h = start; h < start + task.Duration; h++)
//            {
//                if (staffOcc.TryGetValue(staffCode, out var dayMap) &&
//                    dayMap.TryGetValue(day, out var staffHours) &&
//                    staffHours.Contains(h))
//                    return false;
//            }

//            // Check lab conflicts
//            if (task.IsLab && !string.IsNullOrEmpty(task.LabId))
//            {
//                for (int h = start; h < start + task.Duration; h++)
//                {
//                    if (labOcc.TryGetValue(task.LabId, out var labDayMap) &&
//                        labDayMap.TryGetValue(day, out var labHours) &&
//                        labHours.Contains(h))
//                        return false;
//                }

//                // Lab-specific time constraints
//                if (task.Kind == "LAB4" && !(start == 1 || start == 4))
//                    return false;
//            }

//            return true;
//        }

//        private void AssignTask(TaskUnit task, (string day, int start) slot,
//            Dictionary<string, Dictionary<string, HashSet<int>>> staffOcc,
//            Dictionary<string, Dictionary<string, HashSet<int>>> labOcc,
//            Dictionary<string, Dictionary<int, string>> timetableGrid)
//        {
//            var (day, start) = slot;
//            var (_, staffCode) = SplitStaff(task.StaffAssigned);

//            // Update grid
//            for (int h = start; h < start + task.Duration; h++)
//            {
//                timetableGrid[day][h] = $"{task.SubjectCode} ({task.StaffAssigned})";
//            }

//            // Update staff occupancy
//            if (!staffOcc.ContainsKey(staffCode))
//                staffOcc[staffCode] = new Dictionary<string, HashSet<int>>();
//            if (!staffOcc[staffCode].ContainsKey(day))
//                staffOcc[staffCode][day] = new HashSet<int>();

//            for (int h = start; h < start + task.Duration; h++)
//            {
//                staffOcc[staffCode][day].Add(h);
//            }

//            // Update lab occupancy
//            if (task.IsLab && !string.IsNullOrEmpty(task.LabId))
//            {
//                if (!labOcc.ContainsKey(task.LabId))
//                    labOcc[task.LabId] = new Dictionary<string, HashSet<int>>();
//                if (!labOcc[task.LabId].ContainsKey(day))
//                    labOcc[task.LabId][day] = new HashSet<int>();

//                for (int h = start; h < start + task.Duration; h++)
//                {
//                    labOcc[task.LabId][day].Add(h);
//                }
//            }

//            // Update task state
//            task.Day = day;
//            task.StartHour = start;
//            task.IsPlaced = true;
//        }

//        private void UnassignTask(TaskUnit task,
//            Dictionary<string, Dictionary<string, HashSet<int>>> staffOcc,
//            Dictionary<string, Dictionary<string, HashSet<int>>> labOcc,
//            Dictionary<string, Dictionary<int, string>> timetableGrid)
//        {
//            if (!task.IsPlaced) return;

//            var day = task.Day;
//            var start = task.StartHour;
//            var (_, staffCode) = SplitStaff(task.StaffAssigned);

//            // Clear grid
//            for (int h = start; h < start + task.Duration; h++)
//            {
//                timetableGrid[day][h] = "---";
//            }

//            // Clear staff occupancy
//            if (staffOcc.ContainsKey(staffCode) && staffOcc[staffCode].ContainsKey(day))
//            {
//                for (int h = start; h < start + task.Duration; h++)
//                {
//                    staffOcc[staffCode][day].Remove(h);
//                }
//            }

//            // Clear lab occupancy
//            if (task.IsLab && !string.IsNullOrEmpty(task.LabId) &&
//                labOcc.ContainsKey(task.LabId) && labOcc[task.LabId].ContainsKey(day))
//            {
//                for (int h = start; h < start + task.Duration; h++)
//                {
//                    labOcc[task.LabId][day].Remove(h);
//                }
//            }

//            // Clear task state
//            task.IsPlaced = false;
//            task.Day = null;
//            task.StartHour = 0;
//        }

//        #endregion

//        #region Constraint Propagation and Conflict Detection

//        private void BuildConflictGraph(List<TaskUnit> tasks)
//        {
//            // Clear existing conflicts
//            foreach (var task in tasks)
//                task.Conflicts.Clear();

//            // Build conflict relationships
//            for (int i = 0; i < tasks.Count; i++)
//            {
//                for (int j = i + 1; j < tasks.Count; j++)
//                {
//                    var taskA = tasks[i];
//                    var taskB = tasks[j];

//                    if (HaveConflict(taskA, taskB))
//                    {
//                        taskA.Conflicts.Add(taskB);
//                        taskB.Conflicts.Add(taskA);
//                    }
//                }
//            }
//        }

//        private bool HaveConflict(TaskUnit taskA, TaskUnit taskB)
//        {
//            var (_, staffA) = SplitStaff(taskA.StaffAssigned);
//            var (_, staffB) = SplitStaff(taskB.StaffAssigned);

//            // Staff conflict
//            if (staffA == staffB) return true;

//            // Lab conflict
//            if (taskA.IsLab && taskB.IsLab &&
//                !string.IsNullOrEmpty(taskA.LabId) && !string.IsNullOrEmpty(taskB.LabId) &&
//                taskA.LabId == taskB.LabId)
//                return true;

//            // Check domain overlaps
//            foreach (var slotA in taskA.DomainSlots)
//            {
//                foreach (var slotB in taskB.DomainSlots)
//                {
//                    if (slotA.day == slotB.day)
//                    {
//                        int startA = slotA.start;
//                        int endA = startA + taskA.Duration - 1;
//                        int startB = slotB.start;
//                        int endB = startB + taskB.Duration - 1;

//                        if (endA >= startB && endB >= startA)
//                            return true;
//                    }
//                }
//            }

//            return false;
//        }

//        private List<TaskUnit> GetConflictComponent(TaskUnit task)
//        {
//            var visited = new HashSet<TaskUnit>();
//            var stack = new Stack<TaskUnit>();
//            var component = new List<TaskUnit>();

//            stack.Push(task);

//            while (stack.Count > 0)
//            {
//                var current = stack.Pop();
//                if (visited.Contains(current)) continue;

//                visited.Add(current);
//                component.Add(current);

//                foreach (var neighbor in current.Conflicts)
//                {
//                    if (!visited.Contains(neighbor))
//                        stack.Push(neighbor);
//                }
//            }

//            return component;
//        }

//        private bool PropagateConstraints(List<TaskUnit> tasks, TaskUnit assignedTask, (string day, int start) assignedSlot)
//        {
//            // Create snapshot of domains
//            var domainSnapshot = tasks.ToDictionary(t => t, t => t.DomainSlots.ToList());

//            // Set assigned task domain to single slot
//            assignedTask.DomainSlots = new List<(string, int)> { assignedSlot };

//            var queue = new Queue<TaskUnit>();

//            // Add all unplaced tasks except the assigned one to queue
//            foreach (var task in tasks)
//            {
//                if (task != assignedTask && !task.IsPlaced)
//                    queue.Enqueue(task);
//            }

//            while (queue.Count > 0)
//            {
//                var task = queue.Dequeue();
//                if (task.IsPlaced || task == assignedTask) continue;

//                var filteredDomain = new List<(string day, int start)>();

//                foreach (var slot in task.DomainSlots)
//                {
//                    bool hasConflict = false;

//                    // Check conflict with assigned task
//                    if (slot.day == assignedSlot.day)
//                    {
//                        int start1 = slot.start;
//                        int end1 = slot.start + task.Duration - 1;
//                        int start2 = assignedSlot.start;
//                        int end2 = assignedSlot.start + assignedTask.Duration - 1;

//                        bool overlap = end1 >= start2 && end2 >= start1;

//                        if (overlap)
//                        {
//                            var (_, staff1) = SplitStaff(task.StaffAssigned);
//                            var (_, staff2) = SplitStaff(assignedTask.StaffAssigned);

//                            if (staff1 == staff2)
//                                hasConflict = true;
//                            else if (task.IsLab && assignedTask.IsLab &&
//                                     !string.IsNullOrEmpty(task.LabId) &&
//                                     !string.IsNullOrEmpty(assignedTask.LabId) &&
//                                     task.LabId == assignedTask.LabId)
//                                hasConflict = true;
//                        }
//                    }

//                    if (!hasConflict)
//                        filteredDomain.Add(slot);
//                }

//                // Update domain if changed
//                if (filteredDomain.Count != task.DomainSlots.Count)
//                {
//                    task.DomainSlots = filteredDomain;

//                    if (task.DomainSlots.Count == 0)
//                    {
//                        // Restore snapshot and return failure
//                        foreach (var kvp in domainSnapshot)
//                            kvp.Key.DomainSlots = kvp.Value;
//                        return false;
//                    }

//                    // Add conflicted tasks to queue
//                    foreach (var conflictTask in task.Conflicts)
//                    {
//                        if (!conflictTask.IsPlaced && conflictTask != assignedTask && !queue.Contains(conflictTask))
//                        {
//                            queue.Enqueue(conflictTask);
//                        }
//                    }
//                }
//            }

//            return true;
//        }

//        #endregion

//        #region Enhanced Domain Assignment with GA Fallback

//        private bool AssignDomains(List<TaskUnit> tasks,
//            string[] days,
//            int totalHours,
//            Dictionary<string, Dictionary<string, HashSet<int>>> staffOcc,
//            Dictionary<string, Dictionary<string, HashSet<int>>> labOcc,
//            Dictionary<string, Dictionary<int, string>> timetableGrid)
//        {
//            // Initial domain calculation
//            foreach (var task in tasks)
//            {
//                task.DomainSlots.Clear();

//                foreach (var day in days)
//                {
//                    for (int start = 1; start <= totalHours - task.Duration + 1; start++)
//                    {
//                        if (IsFreeAndNoConflict(task, day, start, staffOcc, labOcc, timetableGrid, totalHours))
//                        {
//                            task.DomainSlots.Add((day, start));
//                        }
//                    }
//                }

//                // Enhanced zero-domain handling
//                if (task.DomainSlots.Count == 0)
//                {
//                    Console.WriteLine($"Task {task.SubjectCode} has zero domain slots. Attempting GA fallback...");

//                    // Strategy 1: Try GA fallback for single task
//                    var singleTaskResult = TryGAFallbackSingleTask(task, days, totalHours, staffOcc, labOcc, timetableGrid);
//                    if (singleTaskResult.success)
//                    {
//                        Console.WriteLine($"Single task GA fallback succeeded for {task.SubjectCode}");
//                        task.DomainSlots.Add(singleTaskResult.slot);
//                        continue;
//                    }

//                    // Strategy 2: Try GA fallback for conflict component
//                    var conflictComponent = GetConflictComponent(task);
//                    if (conflictComponent.Count > 1 && conflictComponent.Count <= MAX_GENETIC_COMPONENT_SIZE)
//                    {
//                        var componentResult = TryGAFallbackComponent(conflictComponent, days, totalHours, staffOcc, labOcc, timetableGrid);
//                        if (componentResult.success)
//                        {
//                            Console.WriteLine($"Component GA fallback succeeded for {task.SubjectCode} and {conflictComponent.Count} related tasks");

//                            // Apply GA solution to all tasks in component
//                            foreach (var componentTask in conflictComponent)
//                            {
//                                if (componentResult.assignments.TryGetValue(componentTask, out var assignment))
//                                {
//                                    componentTask.DomainSlots.Clear();
//                                    componentTask.DomainSlots.Add(assignment);
//                                }
//                            }
//                            continue;
//                        }
//                    }

//                    // Both strategies failed
//                    Console.WriteLine($"Both GA fallback strategies failed for {task.SubjectCode}");
//                    return false;
//                }
//            }

//            // Build conflict graph after domain assignment
//            BuildConflictGraph(tasks);
//            return true;
//        }

//        #endregion

//        #region Genetic Algorithm Fallback Methods

//        private (bool success, (string day, int start) slot) TryGAFallbackSingleTask(
//            TaskUnit task,
//            string[] days,
//            int totalHours,
//            Dictionary<string, Dictionary<string, HashSet<int>>> staffOcc,
//            Dictionary<string, Dictionary<string, HashSet<int>>> labOcc,
//            Dictionary<string, Dictionary<int, string>> timetableGrid)
//        {
//            // Create potential slots by relaxing some constraints
//            var potentialSlots = new List<(string day, int start)>();

//            foreach (var day in days)
//            {
//                for (int start = 1; start <= totalHours - task.Duration + 1; start++)
//                {
//                    // Relax constraints - only check basic time and grid availability
//                    bool canPlace = true;

//                    // Check time bounds
//                    if (start < 1 || start + task.Duration - 1 > totalHours)
//                        canPlace = false;

//                    // Check lab-specific constraints
//                    if (task.IsLab && task.Kind == "LAB4" && !(start == 1 || start == 4))
//                        canPlace = false;

//                    if (canPlace)
//                        potentialSlots.Add((day, start));
//                }
//            }

//            if (potentialSlots.Count == 0)
//                return (false, ("", 0));

//            // Simple GA for single task
//            var population = new List<GAIndividual>();

//            // Create initial population
//            for (int i = 0; i < Math.Min(20, potentialSlots.Count); i++)
//            {
//                var individual = new GAIndividual();
//                individual.Assignments[task] = potentialSlots[rng.Next(potentialSlots.Count)];
//                individual.Fitness = EvaluateSingleTaskFitness(task, individual.Assignments[task], staffOcc, labOcc, timetableGrid);
//                population.Add(individual);
//            }

//            // Evolution
//            for (int gen = 0; gen < 50; gen++)
//            {
//                population = population.OrderBy(ind => ind.Fitness).ToList();

//                if (population[0].Fitness == 0)
//                {
//                    return (true, population[0].Assignments[task]);
//                }

//                // Create next generation
//                var nextGen = new List<GAIndividual> { population[0] }; // Elitism

//                while (nextGen.Count < population.Count)
//                {
//                    var parent = population[rng.Next(Math.Min(5, population.Count))]; // Tournament selection
//                    var child = parent.Clone();

//                    // Mutation
//                    if (rng.NextDouble() < 0.3)
//                    {
//                        child.Assignments[task] = potentialSlots[rng.Next(potentialSlots.Count)];
//                    }

//                    child.Fitness = EvaluateSingleTaskFitness(task, child.Assignments[task], staffOcc, labOcc, timetableGrid);
//                    nextGen.Add(child);
//                }

//                population = nextGen;
//            }

//            population = population.OrderBy(ind => ind.Fitness).ToList();

//            // Accept solution if fitness is reasonable (allow some conflicts)
//            if (population[0].Fitness < 5)
//            {
//                return (true, population[0].Assignments[task]);
//            }

//            return (false, ("", 0));
//        }

//        private (bool success, Dictionary<TaskUnit, (string day, int start)> assignments) TryGAFallbackComponent(
//            List<TaskUnit> component,
//            string[] days,
//            int totalHours,
//            Dictionary<string, Dictionary<string, HashSet<int>>> staffOcc,
//            Dictionary<string, Dictionary<string, HashSet<int>>> labOcc,
//            Dictionary<string, Dictionary<int, string>> timetableGrid)
//        {
//            if (component.Count > MAX_GENETIC_COMPONENT_SIZE)
//                return (false, null);

//            var population = new List<GAIndividual>();

//            // Create initial population
//            for (int i = 0; i < GA_POPULATION_SIZE; i++)
//            {
//                var individual = CreateRandomIndividual(component, days, totalHours);
//                if (individual != null)
//                {
//                    individual.Fitness = EvaluateComponentFitness(component, individual, staffOcc, labOcc, timetableGrid);
//                    population.Add(individual);
//                }
//            }

//            if (population.Count == 0)
//                return (false, null);

//            // Evolution
//            for (int gen = 0; gen < GA_MAX_GENERATIONS; gen++)
//            {
//                population = population.OrderBy(ind => ind.Fitness).ToList();

//                if (population[0].Fitness == 0)
//                {
//                    return (true, population[0].Assignments);
//                }

//                // Create next generation
//                var nextGen = new List<GAIndividual> { population[0], population[1] }; // Elitism

//                while (nextGen.Count < population.Count)
//                {
//                    var parent1 = TournamentSelection(population);
//                    var parent2 = TournamentSelection(population);
//                    var (child1, child2) = Crossover(parent1, parent2, component);

//                    Mutate(child1, component, days, totalHours);
//                    Mutate(child2, component, days, totalHours);

//                    child1.Fitness = EvaluateComponentFitness(component, child1, staffOcc, labOcc, timetableGrid);
//                    child2.Fitness = EvaluateComponentFitness(component, child2, staffOcc, labOcc, timetableGrid);

//                    nextGen.Add(child1);
//                    if (nextGen.Count < population.Count)
//                        nextGen.Add(child2);
//                }

//                population = nextGen;
//            }

//            population = population.OrderBy(ind => ind.Fitness).ToList();

//            // Accept solution if fitness is reasonable
//            if (population[0].Fitness < component.Count * 3)
//            {
//                return (true, population[0].Assignments);
//            }

//            return (false, null);
//        }

//        private GAIndividual CreateRandomIndividual(List<TaskUnit> tasks, string[] days, int totalHours)
//        {
//            var individual = new GAIndividual();

//            foreach (var task in tasks)
//            {
//                var validSlots = new List<(string day, int start)>();

//                foreach (var day in days)
//                {
//                    for (int start = 1; start <= totalHours - task.Duration + 1; start++)
//                    {
//                        bool canPlace = true;

//                        // Basic time bounds check
//                        if (start < 1 || start + task.Duration - 1 > totalHours)
//                            canPlace = false;

//                        // Lab-specific constraints
//                        if (task.IsLab && task.Kind == "LAB4" && !(start == 1 || start == 4))
//                            canPlace = false;

//                        if (canPlace)
//                            validSlots.Add((day, start));
//                    }
//                }

//                if (validSlots.Count == 0)
//                    return null;

//                individual.Assignments[task] = validSlots[rng.Next(validSlots.Count)];
//            }

//            return individual;
//        }

//        private int EvaluateSingleTaskFitness(TaskUnit task, (string day, int start) slot,
//            Dictionary<string, Dictionary<string, HashSet<int>>> staffOcc,
//            Dictionary<string, Dictionary<string, HashSet<int>>> labOcc,
//            Dictionary<string, Dictionary<int, string>> timetableGrid)
//        {
//            int penalty = 0;
//            var (_, staffCode) = SplitStaff(task.StaffAssigned);

//            for (int h = slot.start; h < slot.start + task.Duration; h++)
//            {
//                // Grid conflict
//                if (timetableGrid.ContainsKey(slot.day) &&
//                    timetableGrid[slot.day].ContainsKey(h) &&
//                    timetableGrid[slot.day][h] != "---")
//                    penalty += 10;

//                // Staff conflict
//                if (staffOcc.ContainsKey(staffCode) &&
//                    staffOcc[staffCode].ContainsKey(slot.day) &&
//                    staffOcc[staffCode][slot.day].Contains(h))
//                    penalty += 5;

//                // Lab conflict
//                if (task.IsLab && !string.IsNullOrEmpty(task.LabId) &&
//                    labOcc.ContainsKey(task.LabId) &&
//                    labOcc[task.LabId].ContainsKey(slot.day) &&
//                    labOcc[task.LabId][slot.day].Contains(h))
//                    penalty += 5;
//            }

//            return penalty;
//        }

//        private int EvaluateComponentFitness(List<TaskUnit> component, GAIndividual individual,
//            Dictionary<string, Dictionary<string, HashSet<int>>> staffOcc,
//            Dictionary<string, Dictionary<string, HashSet<int>>> labOcc,
//            Dictionary<string, Dictionary<int, string>> timetableGrid)
//        {
//            int penalty = 0;
//            var internalStaffSchedule = new Dictionary<string, Dictionary<string, HashSet<int>>>();
//            var internalLabSchedule = new Dictionary<string, Dictionary<string, HashSet<int>>>();

//            foreach (var task in component)
//            {
//                if (!individual.Assignments.TryGetValue(task, out var slot))
//                    continue;

//                var (_, staffCode) = SplitStaff(task.StaffAssigned);

//                for (int h = slot.start; h < slot.start + task.Duration; h++)
//                {
//                    // External conflicts
//                    if (timetableGrid.ContainsKey(slot.day) &&
//                        timetableGrid[slot.day].ContainsKey(h) &&
//                        timetableGrid[slot.day][h] != "---")
//                        penalty += 10;

//                    if (staffOcc.ContainsKey(staffCode) &&
//                        staffOcc[staffCode].ContainsKey(slot.day) &&
//                        staffOcc[staffCode][slot.day].Contains(h))
//                        penalty += 8;

//                    if (task.IsLab && !string.IsNullOrEmpty(task.LabId) &&
//                        labOcc.ContainsKey(task.LabId) &&
//                        labOcc[task.LabId].ContainsKey(slot.day) &&
//                        labOcc[task.LabId][slot.day].Contains(h))
//                        penalty += 8;

//                    // Internal conflicts
//                    if (!internalStaffSchedule.ContainsKey(staffCode))
//                        internalStaffSchedule[staffCode] = new Dictionary<string, HashSet<int>>();
//                    if (!internalStaffSchedule[staffCode].ContainsKey(slot.day))
//                        internalStaffSchedule[staffCode][slot.day] = new HashSet<int>();

//                    if (internalStaffSchedule[staffCode][slot.day].Contains(h))
//                        penalty += 15;
//                    else
//                        internalStaffSchedule[staffCode][slot.day].Add(h);

//                    if (task.IsLab && !string.IsNullOrEmpty(task.LabId))
//                    {
//                        if (!internalLabSchedule.ContainsKey(task.LabId))
//                            internalLabSchedule[task.LabId] = new Dictionary<string, HashSet<int>>();
//                        if (!internalLabSchedule[task.LabId].ContainsKey(slot.day))
//                            internalLabSchedule[task.LabId][slot.day] = new HashSet<int>();

//                        if (internalLabSchedule[task.LabId][slot.day].Contains(h))
//                            penalty += 15;
//                        else
//                            internalLabSchedule[task.LabId][slot.day].Add(h);
//                    }
//                }
//            }

//            return penalty;
//        }

//        private GAIndividual TournamentSelection(List<GAIndividual> population)
//        {
//            int k = 3; // Tournament size
//            var selected = new List<GAIndividual>();

//            for (int i = 0; i < k; i++)
//            {
//                selected.Add(population[rng.Next(population.Count)]);
//            }

//            return selected.OrderBy(ind => ind.Fitness).First();
//        }

//        private (GAIndividual child1, GAIndividual child2) Crossover(GAIndividual parent1, GAIndividual parent2, List<TaskUnit> tasks)
//        {
//            var child1 = new GAIndividual();
//            var child2 = new GAIndividual();

//            int crossoverPoint = rng.Next(1, tasks.Count);

//            for (int i = 0; i < tasks.Count; i++)
//            {
//                var task = tasks[i];

//                if (i < crossoverPoint)
//                {
//                    if (parent1.Assignments.ContainsKey(task))
//                        child1.Assignments[task] = parent1.Assignments[task];
//                    if (parent2.Assignments.ContainsKey(task))
//                        child2.Assignments[task] = parent2.Assignments[task];
//                }
//                else
//                {
//                    if (parent2.Assignments.ContainsKey(task))
//                        child1.Assignments[task] = parent2.Assignments[task];
//                    if (parent1.Assignments.ContainsKey(task))
//                        child2.Assignments[task] = parent1.Assignments[task];
//                }
//            }

//            return (child1, child2);
//        }

//        private void Mutate(GAIndividual individual, List<TaskUnit> tasks, string[] days, int totalHours)
//        {
//            foreach (var task in tasks)
//            {
//                if (rng.NextDouble() < GA_MUTATION_RATE)
//                {
//                    var validSlots = new List<(string day, int start)>();

//                    foreach (var day in days)
//                    {
//                        for (int start = 1; start <= totalHours - task.Duration + 1; start++)
//                        {
//                            bool canPlace = true;

//                            if (start < 1 || start + task.Duration - 1 > totalHours)
//                                canPlace = false;

//                            if (task.IsLab && task.Kind == "LAB4" && !(start == 1 || start == 4))
//                                canPlace = false;

//                            if (canPlace)
//                                validSlots.Add((day, start));
//                        }
//                    }

//                    if (validSlots.Count > 0)
//                    {
//                        individual.Assignments[task] = validSlots[rng.Next(validSlots.Count)];
//                    }
//                }
//            }
//        }

//        #endregion

//        #region Task Ordering and Selection Heuristics

//        private List<TaskUnit> OrderTasks(List<TaskUnit> tasks)
//        {
//            return tasks.OrderBy(t =>
//            {
//                // Priority: Lab > Embedded Lab > Theory
//                int typePriority = t.Kind switch
//                {
//                    "LAB4" => 0,
//                    "EMB_LAB2" => 1,
//                    "EMB_TH1" => 2,
//                    "TH1" => 3,
//                    _ => 4
//                };
//                return typePriority;
//            })
//            .ThenBy(t => t.DomainSlots.Count) // MRV (Most Constrained Variable)
//            .ThenByDescending(t => t.Conflicts.Count) // Degree heuristic
//            .ToList();
//        }

//        private List<(string day, int start)> OrderDomainByLCV(TaskUnit task, List<TaskUnit> allTasks)
//        {
//            var slotConstraintCount = new Dictionary<(string day, int start), int>();

//            foreach (var slot in task.DomainSlots)
//            {
//                int constraintCount = 0;

//                foreach (var otherTask in allTasks)
//                {
//                    if (otherTask == task || otherTask.IsPlaced) continue;

//                    // Count how many slots this assignment would eliminate from other tasks
//                    foreach (var otherSlot in otherTask.DomainSlots)
//                    {
//                        if (WouldConflict(task, slot, otherTask, otherSlot))
//                        {
//                            constraintCount++;
//                        }
//                    }
//                }

//                slotConstraintCount[slot] = constraintCount;
//            }

//            // Least Constraining Value: choose slots that eliminate fewer options for other tasks
//            return slotConstraintCount.OrderBy(kvp => kvp.Value).Select(kvp => kvp.Key).ToList();
//        }

//        private bool WouldConflict(TaskUnit taskA, (string day, int start) slotA, TaskUnit taskB, (string day, int start) slotB)
//        {
//            if (slotA.day != slotB.day) return false;

//            int startA = slotA.start;
//            int endA = startA + taskA.Duration - 1;
//            int startB = slotB.start;
//            int endB = startB + taskB.Duration - 1;

//            if (!(endA >= startB && endB >= startA)) return false;

//            var (_, staffA) = SplitStaff(taskA.StaffAssigned);
//            var (_, staffB) = SplitStaff(taskB.StaffAssigned);

//            if (staffA == staffB) return true;

//            if (taskA.IsLab && taskB.IsLab &&
//                !string.IsNullOrEmpty(taskA.LabId) && !string.IsNullOrEmpty(taskB.LabId) &&
//                taskA.LabId == taskB.LabId)
//                return true;

//            return false;
//        }

//        #endregion

//        #region Main Backtracking Algorithm

//        private async Task<bool> RecursiveBacktracking(List<TaskUnit> tasks,
//            string[] days,
//            int totalHours,
//            Dictionary<string, Dictionary<string, HashSet<int>>> staffOcc,
//            Dictionary<string, Dictionary<string, HashSet<int>>> labOcc,
//            Dictionary<string, Dictionary<int, string>> timetableGrid,
//            int recursionDepth = 0)
//        {
//            if (recursionDepth > MAX_RECURSION_DEPTH)
//                return false;

//            // Check if all tasks are placed
//            if (tasks.All(t => t.IsPlaced))
//                return true;

//            // Select next task using ordering heuristics
//            var unplacedTasks = tasks.Where(t => !t.IsPlaced).ToList();
//            if (unplacedTasks.Count == 0)
//                return true;

//            var orderedTasks = OrderTasks(unplacedTasks);
//            var selectedTask = orderedTasks.First();

//            // Order domain by Least Constraining Value
//            var orderedDomain = OrderDomainByLCV(selectedTask, tasks);

//            foreach (var slot in orderedDomain)
//            {
//                // Check if slot is still valid
//                if (!IsFreeAndNoConflict(selectedTask, slot.day, slot.start, staffOcc, labOcc, timetableGrid, totalHours))
//                    continue;

//                // Make assignment
//                AssignTask(selectedTask, slot, staffOcc, labOcc, timetableGrid);

//                // Propagate constraints
//                if (!PropagateConstraints(tasks, selectedTask, slot))
//                {
//                    UnassignTask(selectedTask, staffOcc, labOcc, timetableGrid);
//                    continue;
//                }

//                // Recursive call
//                if (await RecursiveBacktracking(tasks, days, totalHours, staffOcc, labOcc, timetableGrid, recursionDepth + 1))
//                    return true;

//                // Backtrack
//                UnassignTask(selectedTask, staffOcc, labOcc, timetableGrid);
//            }

//            return false;
//        }

//        #endregion

//        #region Main API Endpoint

//        [HttpPost("generateCrossDepartmentTimetableBacktracking")]
//        public async Task<IActionResult> GenerateEnhancedTimetable([FromBody] TimetableRequest request)
//        {
//            if (request == null || request.Subjects == null || request.Subjects.Count == 0)
//                return BadRequest(new { message = "❌ Request or subjects missing." });

//            var cs = _configuration.GetConnectionString("DefaultConnection");
//            await using var conn = new NpgsqlConnection(cs);
//            await conn.OpenAsync();

//            string[] DAYS = new[] { "Mon", "Tue", "Wed", "Thu", "Fri" };
//            int HOURS = Math.Max(1, request.TotalHoursPerDay);

//            // Initialize data structures
//            var timetableGrid = DAYS.ToDictionary(d => d, d => Enumerable.Range(1, HOURS).ToDictionary(h => h, _ => "---"));
//            var staffOcc = new Dictionary<string, Dictionary<string, HashSet<int>>>();
//            var labOcc = new Dictionary<string, Dictionary<string, HashSet<int>>>();

//            // Load existing occupancy from database
//            await LoadExistingOccupancy(conn, staffOcc, labOcc, DAYS);

//            // Create task units from subjects
//            var tasks = CreateTaskUnits(request.Subjects);
//            if (tasks.Count == 0)
//                return Ok(new { message = "❌ No valid tasks created from subjects." });

//            // Shuffle tasks for randomization
//            Shuffle(tasks);

//            try
//            {
//                // Enhanced domain assignment with GA fallback
//                if (!AssignDomains(tasks, DAYS, HOURS, staffOcc, labOcc, timetableGrid))
//                {
//                    return Ok(new { message = "❌ Failed to assign domains even with GA fallback." });
//                }

//                // Main backtracking algorithm
//                var solved = await RecursiveBacktracking(tasks, DAYS, HOURS, staffOcc, labOcc, timetableGrid);

//                if (!solved)
//                {
//                    return Ok(new { message = "❌ Could not solve timetable with current constraints." });
//                }

//                // Validate solution
//                if (!ValidateSolution(tasks, HOURS))
//                {
//                    return Ok(new { message = "❌ Generated solution failed validation." });
//                }

//                // Save to database
//                await SaveTimetableToDatabase(conn, request, tasks);

//                return Ok(new
//                {
//                    message = "✅ Enhanced timetable generated successfully with GA fallback support.",
//                    timetable = timetableGrid.Select(kvp => new { Day = kvp.Key, Slots = kvp.Value }),
//                    taskCount = tasks.Count,
//                    placedTasks = tasks.Count(t => t.IsPlaced)
//                });
//            }
//            catch (Exception ex)
//            {
//                return StatusCode(500, new { message = $"❌ Error during timetable generation: {ex.Message}" });
//            }
//        }

//        #endregion

//        #region Helper Methods for Database and Validation

//        private async Task LoadExistingOccupancy(NpgsqlConnection conn,
//            Dictionary<string, Dictionary<string, HashSet<int>>> staffOcc,
//            Dictionary<string, Dictionary<string, HashSet<int>>> labOcc,
//            string[] days)
//        {
//            // Load staff occupancy
//            await using (var cmd = new NpgsqlCommand("SELECT staff_code, day, hour FROM classtimetable", conn))
//            await using (var reader = await cmd.ExecuteReaderAsync())
//            {
//                while (await reader.ReadAsync())
//                {
//                    var staffCode = reader["staff_code"] as string ?? "---";
//                    var day = reader["day"] as string ?? "Mon";
//                    var hour = (int)reader["hour"];

//                    if (!staffOcc.ContainsKey(staffCode))
//                        staffOcc[staffCode] = days.ToDictionary(d => d, _ => new HashSet<int>());
//                    staffOcc[staffCode][day].Add(hour);
//                }
//            }

//            // Load lab occupancy
//            await using (var cmd = new NpgsqlCommand("SELECT lab_id, day, hour FROM labtimetable", conn))
//            await using (var reader = await cmd.ExecuteReaderAsync())
//            {
//                while (await reader.ReadAsync())
//                {
//                    var labId = reader["lab_id"] as string ?? "---";
//                    var day = reader["day"] as string ?? "Mon";
//                    var hour = (int)reader["hour"];

//                    if (!labOcc.ContainsKey(labId))
//                        labOcc[labId] = days.ToDictionary(d => d, _ => new HashSet<int>());
//                    labOcc[labId][day].Add(hour);
//                }
//            }
//        }

//        private List<TaskUnit> CreateTaskUnits(List<SubjectDto> subjects)
//        {
//            var tasks = new List<TaskUnit>();

//            foreach (var subject in subjects.Where(s => !string.IsNullOrWhiteSpace(s.StaffAssigned)))
//            {
//                var type = (subject.SubjectType ?? "theory").Trim().ToLowerInvariant();

//                switch (type)
//                {
//                    case "lab":
//                        tasks.Add(new TaskUnit
//                        {
//                            SubjectCode = subject.SubjectCode ?? "---",
//                            SubjectName = subject.SubjectName ?? "---",
//                            StaffAssigned = subject.StaffAssigned,
//                            LabId = subject.LabId,
//                            IsLab = true,
//                            Duration = 4,
//                            Kind = "LAB4"
//                        });
//                        break;

//                    case "embedded":
//                        tasks.Add(new TaskUnit
//                        {
//                            SubjectCode = subject.SubjectCode ?? "---",
//                            SubjectName = subject.SubjectName ?? "---",
//                            StaffAssigned = subject.StaffAssigned,
//                            LabId = subject.LabId,
//                            IsLab = true,
//                            Duration = 2,
//                            Kind = "EMB_LAB2"
//                        });

//                        for (int i = 0; i < 2; i++)
//                        {
//                            tasks.Add(new TaskUnit
//                            {
//                                SubjectCode = subject.SubjectCode ?? "---",
//                                SubjectName = subject.SubjectName ?? "---",
//                                StaffAssigned = subject.StaffAssigned,
//                                IsLab = false,
//                                Duration = 1,
//                                Kind = "EMB_TH1"
//                            });
//                        }
//                        break;

//                    default: // theory
//                        int creditHours = Math.Max(0, subject.Credit);
//                        for (int i = 0; i < creditHours; i++)
//                        {
//                            tasks.Add(new TaskUnit
//                            {
//                                SubjectCode = subject.SubjectCode ?? "---",
//                                SubjectName = subject.SubjectName ?? "---",
//                                StaffAssigned = subject.StaffAssigned,
//                                IsLab = false,
//                                Duration = 1,
//                                Kind = "TH1"
//                            });
//                        }
//                        break;
//                }
//            }

//            return tasks;
//        }

//        private bool ValidateSolution(List<TaskUnit> tasks, int totalHours)
//        {
//            var staffSchedule = new Dictionary<string, Dictionary<string, HashSet<int>>>();
//            var labSchedule = new Dictionary<string, Dictionary<string, HashSet<int>>>();

//            foreach (var task in tasks)
//            {
//                if (!task.IsPlaced) return false;

//                var (_, staffCode) = SplitStaff(task.StaffAssigned);

//                // Initialize staff schedule tracking
//                if (!staffSchedule.ContainsKey(staffCode))
//                    staffSchedule[staffCode] = new Dictionary<string, HashSet<int>>();
//                if (!staffSchedule[staffCode].ContainsKey(task.Day))
//                    staffSchedule[staffCode][task.Day] = new HashSet<int>();

//                // Initialize lab schedule tracking
//                if (task.IsLab && !string.IsNullOrEmpty(task.LabId))
//                {
//                    if (!labSchedule.ContainsKey(task.LabId))
//                        labSchedule[task.LabId] = new Dictionary<string, HashSet<int>>();
//                    if (!labSchedule[task.LabId].ContainsKey(task.Day))
//                        labSchedule[task.LabId][task.Day] = new HashSet<int>();
//                }

//                // Check each hour of the task
//                for (int h = task.StartHour; h < task.StartHour + task.Duration; h++)
//                {
//                    // Validate time bounds
//                    if (h < 1 || h > totalHours) return false;

//                    // Check staff conflicts
//                    if (staffSchedule[staffCode][task.Day].Contains(h)) return false;
//                    staffSchedule[staffCode][task.Day].Add(h);

//                    // Check lab conflicts
//                    if (task.IsLab && !string.IsNullOrEmpty(task.LabId))
//                    {
//                        if (labSchedule[task.LabId][task.Day].Contains(h)) return false;
//                        labSchedule[task.LabId][task.Day].Add(h);
//                    }
//                }

//                // Validate lab-specific constraints
//                if (task.IsLab && task.Kind == "LAB4" && !(task.StartHour == 1 || task.StartHour == 4))
//                    return false;
//            }

//            return true;
//        }

//        private async Task SaveTimetableToDatabase(NpgsqlConnection conn, TimetableRequest request, List<TaskUnit> tasks)
//        {
//            await using var transaction = await conn.BeginTransactionAsync();

//            // Delete existing entries for this department/year/semester/section
//            await using (var deleteCmd = new NpgsqlCommand(@"
//                DELETE FROM classtimetable 
//                WHERE department_id = @dept AND year = @year AND semester = @sem AND section = @section;
//                DELETE FROM labtimetable 
//                WHERE department = @dept AND year = @year AND semester = @sem AND section = @section;",
//                conn, transaction))
//            {
//                deleteCmd.Parameters.AddWithValue("dept", request.Department ?? "---");
//                deleteCmd.Parameters.AddWithValue("year", request.Year ?? "---");
//                deleteCmd.Parameters.AddWithValue("sem", request.Semester ?? "---");
//                deleteCmd.Parameters.AddWithValue("section", request.Section ?? "---");
//                await deleteCmd.ExecuteNonQueryAsync();
//            }

//            // Insert new timetable entries
//            foreach (var task in tasks.Where(t => t.IsPlaced))
//            {
//                var (staffName, staffCode) = SplitStaff(task.StaffAssigned);

//                for (int h = task.StartHour; h < task.StartHour + task.Duration; h++)
//                {
//                    // Insert into classtimetable
//                    await using (var classCmd = new NpgsqlCommand(@"
//                        INSERT INTO classtimetable 
//                        (staff_name, staff_code, department_id, year, semester, section, day, hour, subject_code, subject_name)
//                        VALUES (@staff_name, @staff_code, @dept, @year, @sem, @section, @day, @hour, @sub_code, @sub_name)",
//                        conn, transaction))
//                    {
//                        classCmd.Parameters.AddWithValue("staff_name", staffName);
//                        classCmd.Parameters.AddWithValue("staff_code", staffCode);
//                        classCmd.Parameters.AddWithValue("dept", request.Department ?? "---");
//                        classCmd.Parameters.AddWithValue("year", request.Year ?? "---");
//                        classCmd.Parameters.AddWithValue("sem", request.Semester ?? "---");
//                        classCmd.Parameters.AddWithValue("section", request.Section ?? "---");
//                        classCmd.Parameters.AddWithValue("day", task.Day);
//                        classCmd.Parameters.AddWithValue("hour", h);
//                        classCmd.Parameters.AddWithValue("sub_code", task.SubjectCode);
//                        classCmd.Parameters.AddWithValue("sub_name", task.SubjectName);
//                        await classCmd.ExecuteNonQueryAsync();
//                    }

//                    // Insert into labtimetable if it's a lab task
//                    if (task.IsLab && !string.IsNullOrEmpty(task.LabId))
//                    {
//                        await using (var labCmd = new NpgsqlCommand(@"
//                            INSERT INTO labtimetable 
//                            (lab_id, subject_code, subject_name, staff_name, department, year, semester, section, day, hour)
//                            VALUES (@lab_id, @sub_code, @sub_name, @staff_name, @dept, @year, @sem, @section, @day, @hour)",
//                            conn, transaction))
//                        {
//                            labCmd.Parameters.AddWithValue("lab_id", task.LabId);
//                            labCmd.Parameters.AddWithValue("sub_code", task.SubjectCode);
//                            labCmd.Parameters.AddWithValue("sub_name", task.SubjectName);
//                            labCmd.Parameters.AddWithValue("staff_name", staffName);
//                            labCmd.Parameters.AddWithValue("dept", request.Department ?? "---");
//                            labCmd.Parameters.AddWithValue("year", request.Year ?? "---");
//                            labCmd.Parameters.AddWithValue("sem", request.Semester ?? "---");
//                            labCmd.Parameters.AddWithValue("section", request.Section ?? "---");
//                            labCmd.Parameters.AddWithValue("day", task.Day);
//                            labCmd.Parameters.AddWithValue("hour", h);
//                            await labCmd.ExecuteNonQueryAsync();
//                        }
//                    }
//                }
//            }

//            await transaction.CommitAsync();
//        }

//        #endregion
//    }
//}
//original code
//using Microsoft.AspNetCore.Mvc;
//using Microsoft.Extensions.Configuration;
//using Npgsql;
//using System;
//using System.Collections.Generic;
//using System.Linq;
//using System.Threading.Tasks;
//using System.Text;

//namespace Timetablegenerator.Controllers
//{
//    /// <summary>
//    /// Enhanced Timetable Controller with advanced constraint satisfaction and genetic algorithm fallback.
//    /// 
//    /// Lab Scheduling Constraints:
//    /// 1. 4-hour labs (LAB4): Must start at hour 1 (morning) or hour 4 (afternoon)
//    ///    - Morning lab (1-4): Class timetable shows 1,2,3,4; Lab timetable shows 1,2,3,4
//    ///    - Afternoon lab (4-7): Class timetable shows 4,5,6,7; Lab timetable shows 5,6,7 (hour 4 controlled by class)
//    /// 2. Embedded subjects: Create 1 lab task (2 hours) + 2 theory tasks (1 hour each)
//    /// 3. Regular theory: Create tasks based on credit hours (1 hour per task)
//    /// </summary>
//    [ApiController]
//    [Route("api/[controller]")]
//    public class EnhancedTimetableController : ControllerBase
//    {
//        private readonly IConfiguration _configuration;
//        private static readonly Random rng = new Random();
//        private const int MAX_RECURSION_DEPTH = 900;
//        private int recursionCounter = 0;
//        private const int MAX_GENETIC_COMPONENT_SIZE = 10; // Reduced for better performance
//        private const int GA_MAX_GENERATIONS = 100; 
//        private const int GA_POPULATION_SIZE = 50;
//        private const double GA_MUTATION_RATE = 0.07; // Reduced for better convergence

//        public EnhancedTimetableController(IConfiguration configuration)
//        {
//            _configuration = configuration;
//        }

//        #region DTOs and Data Structures

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
//            public string SubjectType { get; set; } // "theory", "lab", "embedded"
//            public int Credit { get; set; }
//            public string StaffAssigned { get; set; }
//            public string LabId { get; set; }
//        }

//        public class TaskUnit
//        {
//            public string SubjectCode { get; set; }
//            public string SubjectName { get; set; }
//            public string StaffAssigned { get; set; }
//            public string LabId { get; set; }
//            public bool IsLab { get; set; }
//            public int Duration { get; set; }
//            public string Kind { get; set; } // "LAB4", "EMB_LAB2", "EMB_TH1", "TH1"

//            // Assignment state
//            public string Day { get; set; }
//            public int StartHour { get; set; }
//            public bool IsPlaced { get; set; } = false;

//            // Constraint satisfaction properties
//            public List<(string day, int start)> DomainSlots { get; set; } = new();
//            public List<TaskUnit> Conflicts { get; set; } = new();
//            public (string day, int start) AssignedSlot => IsPlaced ? (Day, StartHour) : ("", 0);

//            // Compatibility property for original code patterns
//            public List<(string day, int start)> Domain
//            {
//                get => DomainSlots;
//                set => DomainSlots = value;
//            }

//            public TaskUnit Clone()
//            {
//                return new TaskUnit
//                {
//                    SubjectCode = this.SubjectCode,
//                    SubjectName = this.SubjectName,
//                    StaffAssigned = this.StaffAssigned,
//                    LabId = this.LabId,
//                    IsLab = this.IsLab,
//                    Duration = this.Duration,
//                    Kind = this.Kind,
//                    Day = this.Day,
//                    StartHour = this.StartHour,
//                    IsPlaced = this.IsPlaced,
//                    DomainSlots = new List<(string, int)>(this.DomainSlots)
//                };
//            }
//        }

//        public class GAIndividual
//        {
//            public Dictionary<TaskUnit, (string day, int start)> Assignments { get; set; } = new();
//            public int Fitness { get; set; } = int.MaxValue;

//            public GAIndividual Clone()
//            {
//                return new GAIndividual
//                {
//                    Assignments = new Dictionary<TaskUnit, (string, int)>(this.Assignments),
//                    Fitness = this.Fitness
//                };
//            }
//        }

//        #endregion

//        #region Helper Methods

//        private void Shuffle<T>(IList<T> list)
//        {
//            int n = list.Count;
//            while (n > 1)
//            {
//                n--;
//                int k = rng.Next(n + 1);
//                (list[k], list[n]) = (list[n], list[k]);
//            }
//        }

//        private (string staffName, string staffCode) SplitStaff(string staffAssigned)
//        {
//            if (string.IsNullOrWhiteSpace(staffAssigned))
//                return ("---", "---");

//            var name = staffAssigned;
//            var code = staffAssigned;

//            if (staffAssigned.Contains("("))
//            {
//                var parts = staffAssigned.Split('(', StringSplitOptions.TrimEntries);
//                name = parts[0].Trim();
//                code = parts.Length > 1 ? parts[1].Replace(")", "").Trim() : "---";
//            }

//            if (string.IsNullOrEmpty(code))
//                code = "---";

//            return (name, code);
//        }

//        private bool IsFreeAndNoConflict(TaskUnit task, string day, int start,
//            Dictionary<string, Dictionary<string, HashSet<int>>> staffOcc,
//            Dictionary<string, Dictionary<string, HashSet<int>>> labOcc,
//            Dictionary<string, Dictionary<int, string>> timetableGrid,
//            int totalHours)
//        {
//            // Validate input parameters
//            if (string.IsNullOrEmpty(day) || start < 1 || task == null)
//                return false;

//            // Check time bounds with 1-based indexing
//            if (start < 1 || start + task.Duration - 1 > totalHours)
//                return false;

//            // Enhanced lab-specific time constraints based on duration
//            if (task.IsLab)
//            {
//                if (task.Duration == 4)
//                {
//                    // 4-hour labs: can start at hour 1 (morning: 1-4) or hour 4 (afternoon: 4-7)
//                    if (!(start == 1 || start == 4))
//                        return false;
//                }
//                else if (task.Duration == 3)
//                {
//                    // 3-hour labs: can start at hour 1 (1-3), hour 4 (4-6), or hour 5 (5-7)
//                    if (!(start == 1 || start == 4 || start == 5))
//                        return false;
//                }
//                else if (task.Duration == 2)
//                {
//                    // 2-hour embedded labs: more flexible, can start at hours 1-6
//                    if (start < 1 || start > 6)
//                        return false;
//                }
//            }

//            // Check grid availability for all hours of the task
//            if (!timetableGrid.ContainsKey(day))
//                return false;

//            for (int h = start; h < start + task.Duration; h++)
//            {
//                if (!timetableGrid[day].ContainsKey(h) || timetableGrid[day][h] != "---")
//                    return false;
//            }

//            // Check staff conflicts
//            var (_, staffCode) = SplitStaff(task.StaffAssigned);
//            if (!string.IsNullOrEmpty(staffCode))
//            {
//                for (int h = start; h < start + task.Duration; h++)
//                {
//                    if (staffOcc.TryGetValue(staffCode, out var dayMap) &&
//                        dayMap.TryGetValue(day, out var staffHours) &&
//                        staffHours.Contains(h))
//                        return false;
//                }
//            }

//            // Check lab conflicts - FIXED: Handle afternoon 4-hour labs correctly
//            if (task.IsLab && !string.IsNullOrEmpty(task.LabId))
//            {
//                // For 4-hour labs starting at hour 4 (afternoon), only check lab hours 5,6,7
//                // Hour 4 is controlled by class timetable, not lab timetable
//                if (task.Duration == 4 && start == 4)
//                {
//                    Console.WriteLine($"🔍 Checking afternoon 4-hour lab {task.SubjectCode} at {day} hour 4: checking lab hours 5,6,7 only");
//                    // Afternoon 4-hour lab: check lab occupancy for hours 5,6,7 only
//                    for (int h = 5; h <= 7; h++)
//                    {
//                        if (labOcc.TryGetValue(task.LabId, out var labDayMap) &&
//                            labDayMap.TryGetValue(day, out var labHours) &&
//                            labHours.Contains(h))
//                        {
//                            Console.WriteLine($"❌ Afternoon lab conflict: {task.SubjectCode} blocked by lab hour {h} in {task.LabId}");
//                            return false;
//                        }
//                    }
//                    Console.WriteLine($"✅ Afternoon lab slot {day} hour 4 is free for {task.SubjectCode}");
//                }
//                else
//                {
//                    // Normal lab conflict check for all other cases
//                    for (int h = start; h < start + task.Duration; h++)
//                    {
//                        if (labOcc.TryGetValue(task.LabId, out var labDayMap) &&
//                            labDayMap.TryGetValue(day, out var labHours) &&
//                            labHours.Contains(h))
//                            return false;
//                    }
//                }
//            }

//            return true;
//        }

//        private void AssignTask(TaskUnit task, (string day, int start) slot,
//            Dictionary<string, Dictionary<string, HashSet<int>>> staffOcc,
//            Dictionary<string, Dictionary<string, HashSet<int>>> labOcc,
//            Dictionary<string, Dictionary<int, string>> timetableGrid)
//        {
//            var (day, start) = slot;
//            var (_, staffCode) = SplitStaff(task.StaffAssigned);

//            // Update grid
//            for (int h = start; h < start + task.Duration; h++)
//            {
//                timetableGrid[day][h] = $"{task.SubjectCode} ({task.StaffAssigned})";
//            }

//            // Update staff occupancy
//            if (!staffOcc.ContainsKey(staffCode))
//                staffOcc[staffCode] = new Dictionary<string, HashSet<int>>();
//            if (!staffOcc[staffCode].ContainsKey(day))
//                staffOcc[staffCode][day] = new HashSet<int>();

//            for (int h = start; h < start + task.Duration; h++)
//            {
//                staffOcc[staffCode][day].Add(h);
//            }

//            // Update lab occupancy - FIXED: Handle afternoon 4-hour labs correctly
//            if (task.IsLab && !string.IsNullOrEmpty(task.LabId))
//            {
//                if (!labOcc.ContainsKey(task.LabId))
//                    labOcc[task.LabId] = new Dictionary<string, HashSet<int>>();
//                if (!labOcc[task.LabId].ContainsKey(day))
//                    labOcc[task.LabId][day] = new HashSet<int>();

//                // For 4-hour labs starting at hour 4 (afternoon), only mark lab hours 5,6,7
//                // Hour 4 is controlled by class timetable, not lab timetable
//                if (task.Duration == 4 && start == 4)
//                {
//                    // Afternoon 4-hour lab: mark lab occupancy for hours 5,6,7 only
//                    for (int h = 5; h <= 7; h++)
//                    {
//                        labOcc[task.LabId][day].Add(h);
//                    }
//                }
//                else
//                {
//                    // Normal lab occupancy for all other cases
//                    for (int h = start; h < start + task.Duration; h++)
//                    {
//                        labOcc[task.LabId][day].Add(h);
//                    }
//                }
//            }

//            // Update task state
//            task.Day = day;
//            task.StartHour = start;
//            task.IsPlaced = true;
//        }

//        private void UnassignTask(TaskUnit task,
//            Dictionary<string, Dictionary<string, HashSet<int>>> staffOcc,
//            Dictionary<string, Dictionary<string, HashSet<int>>> labOcc,
//            Dictionary<string, Dictionary<int, string>> timetableGrid)
//        {
//            if (!task.IsPlaced) return;

//            var day = task.Day;
//            var start = task.StartHour;
//            var (_, staffCode) = SplitStaff(task.StaffAssigned);

//            // Clear grid
//            for (int h = start; h < start + task.Duration; h++)
//            {
//                timetableGrid[day][h] = "---";
//            }

//            // Clear staff occupancy
//            if (staffOcc.ContainsKey(staffCode) && staffOcc[staffCode].ContainsKey(day))
//            {
//                for (int h = start; h < start + task.Duration; h++)
//                {
//                    staffOcc[staffCode][day].Remove(h);
//                }
//            }

//            // Clear lab occupancy - FIXED: Handle afternoon 4-hour labs correctly
//            if (task.IsLab && !string.IsNullOrEmpty(task.LabId) &&
//                labOcc.ContainsKey(task.LabId) && labOcc[task.LabId].ContainsKey(day))
//            {
//                // For 4-hour labs starting at hour 4 (afternoon), only clear lab hours 5,6,7
//                // Hour 4 is controlled by class timetable, not lab timetable
//                if (task.Duration == 4 && start == 4)
//                {
//                    // Afternoon 4-hour lab: clear lab occupancy for hours 5,6,7 only
//                    for (int h = 5; h <= 7; h++)
//                    {
//                        labOcc[task.LabId][day].Remove(h);
//                    }
//                }
//                else
//                {
//                    // Normal lab occupancy clearing for all other cases
//                    for (int h = start; h < start + task.Duration; h++)
//                    {
//                        labOcc[task.LabId][day].Remove(h);
//                    }
//                }
//            }

//            // Clear task state
//            task.IsPlaced = false;
//            task.Day = null;
//            task.StartHour = 0;
//        }

//        #endregion

//        #region Constraint Propagation and Conflict Detection

//        private void BuildConflictGraph(List<TaskUnit> tasks)
//        {
//            // Clear existing conflicts
//            foreach (var task in tasks)
//                task.Conflicts.Clear();

//            // Build conflict relationships
//            for (int i = 0; i < tasks.Count; i++)
//            {
//                for (int j = i + 1; j < tasks.Count; j++)
//                {
//                    var taskA = tasks[i];
//                    var taskB = tasks[j];

//                    if (HaveConflict(taskA, taskB))
//                    {
//                        taskA.Conflicts.Add(taskB);
//                        taskB.Conflicts.Add(taskA);
//                    }
//                }
//            }
//        }

//        private bool HaveConflict(TaskUnit taskA, TaskUnit taskB)
//        {
//            var (_, staffA) = SplitStaff(taskA.StaffAssigned);
//            var (_, staffB) = SplitStaff(taskB.StaffAssigned);

//            // Staff conflict
//            if (staffA == staffB) return true;

//            // Lab conflict
//            if (taskA.IsLab && taskB.IsLab &&
//                !string.IsNullOrEmpty(taskA.LabId) && !string.IsNullOrEmpty(taskB.LabId) &&
//                taskA.LabId == taskB.LabId)
//                return true;

//            // Check domain overlaps
//            foreach (var slotA in taskA.DomainSlots)
//            {
//                foreach (var slotB in taskB.DomainSlots)
//                {
//                    if (slotA.day == slotB.day)
//                    {
//                        int startA = slotA.start;
//                        int endA = startA + taskA.Duration - 1;
//                        int startB = slotB.start;
//                        int endB = startB + taskB.Duration - 1;

//                        if (endA >= startB && endB >= startA)
//                            return true;
//                    }
//                }
//            }

//            return false;
//        }

//        private List<TaskUnit> GetConflictComponent(TaskUnit task)
//        {
//            var visited = new HashSet<TaskUnit>();
//            var stack = new Stack<TaskUnit>();
//            var component = new List<TaskUnit>();

//            stack.Push(task);

//            while (stack.Count > 0)
//            {
//                var current = stack.Pop();
//                if (visited.Contains(current)) continue;

//                visited.Add(current);
//                component.Add(current);

//                foreach (var neighbor in current.Conflicts)
//                {
//                    if (!visited.Contains(neighbor))
//                        stack.Push(neighbor);
//                }
//            }

//            return component;
//        }

//        private void ResetConflictComponent(List<TaskUnit> component,
//            string[] days,
//            int totalHours,
//            Dictionary<string, Dictionary<string, HashSet<int>>> staffOcc,
//            Dictionary<string, Dictionary<string, HashSet<int>>> labOcc,
//            Dictionary<string, Dictionary<int, string>> timetableGrid)
//        {
//            // Reset only the conflicted tasks, preserving feasible domains where possible
//            foreach (var task in component)
//            {
//                if (task.IsPlaced)
//                {
//                    // Unassign task but preserve domain if it had valid slots
//                    var originalDomain = task.DomainSlots.ToList();
//                    UnassignTask(task, staffOcc, labOcc, timetableGrid);

//                    // Recalculate domain, but keep original slots that are still valid
//                    task.DomainSlots.Clear();
//                    foreach (var day in days)
//                    {
//                        for (int start = 1; start <= totalHours - task.Duration + 1; start++)
//                        {
//                            if (IsFreeAndNoConflict(task, day, start, staffOcc, labOcc, timetableGrid, totalHours))
//                            {
//                                task.DomainSlots.Add((day, start));
//                            }
//                        }
//                    }

//                    // If new domain is empty but original had slots, try to preserve some
//                    if (task.DomainSlots.Count == 0 && originalDomain.Count > 0)
//                    {
//                        // Add back original slots that don't conflict with currently placed tasks
//                        foreach (var slot in originalDomain)
//                        {
//                            bool stillValid = true;
//                            for (int h = slot.start; h < slot.start + task.Duration; h++)
//                            {
//                                if (timetableGrid.ContainsKey(slot.day) &&
//                                    timetableGrid[slot.day].ContainsKey(h) &&
//                                    timetableGrid[slot.day][h] != "---")
//                                {
//                                    stillValid = false;
//                                    break;
//                                }
//                            }
//                            if (stillValid)
//                                task.DomainSlots.Add(slot);
//                        }
//                    }
//                }
//            }
//        }

//        private bool PropagateConstraints(List<TaskUnit> tasks, TaskUnit assignedTask, (string day, int start) assignedSlot)
//        {
//            // Create snapshot of domains before propagation
//            var domainSnapshot = tasks.ToDictionary(t => t, t => t.DomainSlots.ToList());

//            // Set assigned task domain to single slot
//            assignedTask.DomainSlots = new List<(string, int)> { assignedSlot };

//            var queue = new Queue<TaskUnit>();
//            var processedTasks = new HashSet<TaskUnit>();

//            // Add all unplaced conflicted tasks to queue
//            foreach (var task in tasks)
//            {
//                if (task != assignedTask && !task.IsPlaced && task.Conflicts.Contains(assignedTask))
//                    queue.Enqueue(task);
//            }

//            while (queue.Count > 0)
//            {
//                var task = queue.Dequeue();
//                if (task.IsPlaced || task == assignedTask || processedTasks.Contains(task))
//                    continue;

//                processedTasks.Add(task);
//                var originalDomainSize = task.DomainSlots.Count;
//                var filteredDomain = new List<(string day, int start)>();

//                // Only prune conflicting slots, not entire domain
//                foreach (var slot in task.DomainSlots)
//                {
//                    bool hasConflict = false;

//                    // Check conflict with assigned task only if on same day
//                    if (slot.day == assignedSlot.day)
//                    {
//                        int start1 = slot.start;
//                        int end1 = slot.start + task.Duration - 1;
//                        int start2 = assignedSlot.start;
//                        int end2 = assignedSlot.start + assignedTask.Duration - 1;

//                        // Check time overlap
//                        bool timeOverlap = end1 >= start2 && end2 >= start1;

//                        if (timeOverlap)
//                        {
//                            var (_, staff1) = SplitStaff(task.StaffAssigned);
//                            var (_, staff2) = SplitStaff(assignedTask.StaffAssigned);

//                            // Staff conflict
//                            if (staff1 == staff2)
//                                hasConflict = true;
//                            // Lab conflict
//                            else if (task.IsLab && assignedTask.IsLab &&
//                                     !string.IsNullOrEmpty(task.LabId) &&
//                                     !string.IsNullOrEmpty(assignedTask.LabId) &&
//                                     task.LabId == assignedTask.LabId)
//                                hasConflict = true;
//                        }
//                    }

//                    if (!hasConflict)
//                        filteredDomain.Add(slot);
//                }

//                // Update domain only if slots were actually pruned
//                if (filteredDomain.Count < originalDomainSize)
//                {
//                    task.DomainSlots = filteredDomain;

//                    // If domain becomes empty, restore all domains and fail
//                    if (task.DomainSlots.Count == 0)
//                    {
//                        foreach (var kvp in domainSnapshot)
//                            kvp.Key.DomainSlots = kvp.Value;
//                        return false;
//                    }

//                    // Add neighbors of affected task to queue for further propagation
//                    foreach (var neighbor in task.Conflicts)
//                    {
//                        if (!neighbor.IsPlaced && neighbor != assignedTask && !queue.Contains(neighbor) && !processedTasks.Contains(neighbor))
//                        {
//                            queue.Enqueue(neighbor);
//                        }
//                    }
//                }
//            }

//            return true;
//        }

//        private bool TryReschedulingToMakeRoom(TaskUnit newTask, List<TaskUnit> allTasks,
//            string[] days, int totalHours,
//            Dictionary<string, Dictionary<string, HashSet<int>>> staffOcc,
//            Dictionary<string, Dictionary<string, HashSet<int>>> labOcc,
//            Dictionary<string, Dictionary<int, string>> timetableGrid)
//        {
//            Console.WriteLine($"Attempting rescheduling for {newTask.SubjectCode} (Duration: {newTask.Duration}, IsLab: {newTask.IsLab})");

//            // Find potential slots where this task could fit if we move other tasks
//            var potentialSlots = new List<(string day, int start, List<TaskUnit> conflictingTasks)>();

//            foreach (var day in days)
//            {
//                for (int start = 1; start <= totalHours - newTask.Duration + 1; start++)
//                {
//                    // Check lab-specific constraints first
//                    if (newTask.IsLab)
//                    {
//                        if (newTask.Duration == 4 && !(start == 1 || start == 4))
//                            continue;
//                        if (newTask.Duration == 3 && !(start == 1 || start == 4 || start == 5))
//                            continue;
//                        if (newTask.Duration == 2 && (start < 1 || start > 6))
//                            continue;
//                    }

//                    // Find what tasks are blocking this slot
//                    var conflictingTasks = new List<TaskUnit>();
//                    var (_, newTaskStaffCode) = SplitStaff(newTask.StaffAssigned);

//                    for (int h = start; h < start + newTask.Duration; h++)
//                    {
//                        // Check grid conflicts
//                        if (timetableGrid.ContainsKey(day) && timetableGrid[day].ContainsKey(h) && timetableGrid[day][h] != "---")
//                        {
//                            // Find the task occupying this slot
//                            var occupyingTask = allTasks.FirstOrDefault(t =>
//                                t.IsPlaced && t.Day == day &&
//                                t.StartHour <= h && h < t.StartHour + t.Duration);

//                            if (occupyingTask != null && !conflictingTasks.Contains(occupyingTask))
//                            {
//                                conflictingTasks.Add(occupyingTask);
//                            }
//                        }

//                        // Check staff conflicts
//                        if (staffOcc.TryGetValue(newTaskStaffCode, out var staffDayMap) &&
//                            staffDayMap.TryGetValue(day, out var staffHours) &&
//                            staffHours.Contains(h))
//                        {
//                            // Find staff conflicting task
//                            var staffConflictTask = allTasks.FirstOrDefault(t =>
//                                t.IsPlaced && t.Day == day &&
//                                t.StartHour <= h && h < t.StartHour + t.Duration &&
//                                SplitStaff(t.StaffAssigned).Item2 == newTaskStaffCode);

//                            if (staffConflictTask != null && !conflictingTasks.Contains(staffConflictTask))
//                            {
//                                conflictingTasks.Add(staffConflictTask);
//                            }
//                        }

//                        // Check lab conflicts
//                        if (newTask.IsLab && !string.IsNullOrEmpty(newTask.LabId) &&
//                            labOcc.TryGetValue(newTask.LabId, out var labDayMap) &&
//                            labDayMap.TryGetValue(day, out var labHours) &&
//                            labHours.Contains(h))
//                        {
//                            // Find lab conflicting task
//                            var labConflictTask = allTasks.FirstOrDefault(t =>
//                                t.IsPlaced && t.IsLab && t.Day == day &&
//                                t.StartHour <= h && h < t.StartHour + t.Duration &&
//                                t.LabId == newTask.LabId);

//                            if (labConflictTask != null && !conflictingTasks.Contains(labConflictTask))
//                            {
//                                conflictingTasks.Add(labConflictTask);
//                            }
//                        }
//                    }

//                    if (conflictingTasks.Count > 0 && conflictingTasks.Count <= 3) // Limit rescheduling complexity
//                    {
//                        potentialSlots.Add((day, start, conflictingTasks));
//                    }
//                }
//            }

//            // Sort potential slots by number of conflicts (prefer fewer conflicts)
//            potentialSlots = potentialSlots.OrderBy(slot => slot.conflictingTasks.Count).ToList();

//            Console.WriteLine($"Found {potentialSlots.Count} potential rescheduling opportunities");

//            // Try each potential slot, prioritizing easier rescheduling scenarios
//            foreach (var (day, start, conflictingTasks) in potentialSlots)
//            {
//                Console.WriteLine($"Trying to reschedule {conflictingTasks.Count} tasks to make room at {day} hour {start}");

//                // Skip if trying to reschedule labs with new theory or vice versa (harder constraints)
//                if (newTask.IsLab && conflictingTasks.Any(t => t.IsLab))
//                {
//                    bool shouldSkip = false;
//                    foreach (var conflictTask in conflictingTasks.Where(t => t.IsLab))
//                    {
//                        // Don't try to reschedule 4-hour labs easily - they have strict constraints
//                        if (conflictTask.Duration == 4)
//                        {
//                            shouldSkip = true;
//                            break;
//                        }
//                    }
//                    if (shouldSkip)
//                    {
//                        Console.WriteLine($"  Skipping - involves rescheduling constrained lab tasks");
//                        continue;
//                    }
//                }

//                // Store original positions
//                var originalPositions = conflictingTasks.ToDictionary(t => t, t => (t.Day, t.StartHour));

//                // Temporarily unassign conflicting tasks
//                foreach (var conflictTask in conflictingTasks)
//                {
//                    UnassignTask(conflictTask, staffOcc, labOcc, timetableGrid);
//                }

//                // Try to find new positions for all conflicting tasks
//                bool canRescheduleAll = true;
//                var newPositions = new Dictionary<TaskUnit, (string day, int start)>();

//                foreach (var conflictTask in conflictingTasks)
//                {
//                    var foundNewSlot = false;

//                    // Try to find a new slot for this task
//                    foreach (var tryDay in days)
//                    {
//                        for (int tryStart = 1; tryStart <= totalHours - conflictTask.Duration + 1; tryStart++)
//                        {
//                            if (IsFreeAndNoConflict(conflictTask, tryDay, tryStart, staffOcc, labOcc, timetableGrid, totalHours))
//                            {
//                                newPositions[conflictTask] = (tryDay, tryStart);
//                                AssignTask(conflictTask, (tryDay, tryStart), staffOcc, labOcc, timetableGrid);
//                                foundNewSlot = true;
//                                Console.WriteLine($"  Rescheduled {conflictTask.SubjectCode} from {originalPositions[conflictTask].Day} {originalPositions[conflictTask].StartHour} to {tryDay} {tryStart}");
//                                break;
//                            }
//                        }
//                        if (foundNewSlot) break;
//                    }

//                    if (!foundNewSlot)
//                    {
//                        canRescheduleAll = false;
//                        Console.WriteLine($"  Cannot reschedule {conflictTask.SubjectCode}");
//                        break;
//                    }
//                }

//                if (canRescheduleAll)
//                {
//                    Console.WriteLine($"✅ Successfully rescheduled all conflicting tasks for slot {day} {start}");
//                    return true;
//                }
//                else
//                {
//                    // Restore original positions
//                    Console.WriteLine($"❌ Failed to reschedule all tasks, restoring original positions");
//                    foreach (var conflictTask in conflictingTasks)
//                    {
//                        UnassignTask(conflictTask, staffOcc, labOcc, timetableGrid);
//                    }

//                    foreach (var conflictTask in conflictingTasks)
//                    {
//                        var (originalDay, originalStart) = originalPositions[conflictTask];
//                        AssignTask(conflictTask, (originalDay, originalStart), staffOcc, labOcc, timetableGrid);
//                    }
//                }
//            }

//            // Try advanced rescheduling with chain moves (move A to B, move B to C, etc.)
//            Console.WriteLine($"Attempting advanced chain rescheduling for {newTask.SubjectCode}");
//            if (TryChainRescheduling(newTask, allTasks, days, totalHours, staffOcc, labOcc, timetableGrid))
//            {
//                return true;
//            }

//            Console.WriteLine($"❌ No successful rescheduling found for {newTask.SubjectCode}");
//            return false;
//        }

//        private bool TryChainRescheduling(TaskUnit newTask, List<TaskUnit> allTasks,
//            string[] days, int totalHours,
//            Dictionary<string, Dictionary<string, HashSet<int>>> staffOcc,
//            Dictionary<string, Dictionary<string, HashSet<int>>> labOcc,
//            Dictionary<string, Dictionary<int, string>> timetableGrid)
//        {
//            // Try chain rescheduling: move task A to make room, which creates room for task B, etc.
//            var placedTasks = allTasks.Where(t => t.IsPlaced).ToList();

//            foreach (var taskToMove in placedTasks.Take(Math.Min(5, placedTasks.Count))) // Limit to avoid infinite loops
//            {
//                // Store original position
//                var originalDay = taskToMove.Day;
//                var originalStart = taskToMove.StartHour;

//                // Temporarily unassign this task
//                UnassignTask(taskToMove, staffOcc, labOcc, timetableGrid);

//                // Check if new task can fit in the freed space
//                var canFitNewTask = false;
//                for (int checkStart = Math.Max(1, originalStart - newTask.Duration + 1);
//                     checkStart <= Math.Min(totalHours - newTask.Duration + 1, originalStart + taskToMove.Duration - 1);
//                     checkStart++)
//                {
//                    if (IsFreeAndNoConflict(newTask, originalDay, checkStart, staffOcc, labOcc, timetableGrid, totalHours))
//                    {
//                        canFitNewTask = true;
//                        break;
//                    }
//                }

//                if (canFitNewTask)
//                {
//                    // Try to find new position for moved task
//                    foreach (var tryDay in days)
//                    {
//                        for (int tryStart = 1; tryStart <= totalHours - taskToMove.Duration + 1; tryStart++)
//                        {
//                            if (IsFreeAndNoConflict(taskToMove, tryDay, tryStart, staffOcc, labOcc, timetableGrid, totalHours))
//                            {
//                                // Assign moved task to new position
//                                AssignTask(taskToMove, (tryDay, tryStart), staffOcc, labOcc, timetableGrid);

//                                // Verify new task can still fit
//                                for (int checkStart = Math.Max(1, originalStart - newTask.Duration + 1);
//                                     checkStart <= Math.Min(totalHours - newTask.Duration + 1, originalStart + taskToMove.Duration - 1);
//                                     checkStart++)
//                                {
//                                    if (IsFreeAndNoConflict(newTask, originalDay, checkStart, staffOcc, labOcc, timetableGrid, totalHours))
//                                    {
//                                        Console.WriteLine($"✅ Chain rescheduling successful: moved {taskToMove.SubjectCode} from {originalDay} {originalStart} to {tryDay} {tryStart}");
//                                        return true;
//                                    }
//                                }

//                                // If new task doesn't fit, restore and try next position
//                                UnassignTask(taskToMove, staffOcc, labOcc, timetableGrid);
//                                AssignTask(taskToMove, (originalDay, originalStart), staffOcc, labOcc, timetableGrid);
//                                return false; // Exit this attempt
//                            }
//                        }
//                    }
//                }

//                // Restore original position
//                AssignTask(taskToMove, (originalDay, originalStart), staffOcc, labOcc, timetableGrid);
//            }

//            return false;
//        }

//        #endregion

//        #region Enhanced Domain Assignment with GA Fallback

//        private bool AssignDomains(List<TaskUnit> tasks,
//            string[] days,
//            int totalHours,
//            Dictionary<string, Dictionary<string, HashSet<int>>> staffOcc,
//            Dictionary<string, Dictionary<string, HashSet<int>>> labOcc,
//            Dictionary<string, Dictionary<int, string>> timetableGrid)
//        {
//            // Simple domain assignment like original code for better accuracy
//            foreach (var task in tasks)
//            {
//                task.DomainSlots.Clear();

//                foreach (var day in days)
//                {
//                    for (int start = 1; start <= totalHours - task.Duration + 1; start++)
//                    {
//                        if (IsFreeAndNoConflict(task, day, start, staffOcc, labOcc, timetableGrid, totalHours))
//                        {
//                            task.DomainSlots.Add((day, start));
//                        }
//                    }
//                }

//                // Enhanced rescheduling approach instead of immediate failure
//                if (task.DomainSlots.Count == 0)
//                {
//                    Console.WriteLine($"No initial slot for task {task.SubjectCode}. Attempting rescheduling...");

//                    // Try rescheduling existing tasks to make room
//                    if (TryReschedulingToMakeRoom(task, tasks, days, totalHours, staffOcc, labOcc, timetableGrid))
//                    {
//                        Console.WriteLine($"✅ Rescheduling successful for {task.SubjectCode}");
//                        // Recalculate domain after rescheduling
//                        task.DomainSlots.Clear();
//                        foreach (var day in days)
//                        {
//                            for (int start = 1; start <= totalHours - task.Duration + 1; start++)
//                            {
//                                if (IsFreeAndNoConflict(task, day, start, staffOcc, labOcc, timetableGrid, totalHours))
//                                {
//                                    task.DomainSlots.Add((day, start));
//                                }
//                            }
//                        }

//                        if (task.DomainSlots.Count == 0)
//                        {
//                            Console.WriteLine($"❌ Rescheduling failed for {task.SubjectCode} - still no valid slots");
//                            return false;
//                        }
//                    }
//                    else
//                    {
//                        Console.WriteLine($"❌ Rescheduling failed for {task.SubjectCode}");
//                        return false;
//                    }
//                }

//                // Shuffle domain slots like original code
//                Shuffle(task.DomainSlots);
//            }

//            return true;
//        }

//        #endregion

//        #region Genetic Algorithm Fallback Methods

//        private (bool success, (string day, int start) slot) TryGAFallbackSingleTask(
//            TaskUnit task,
//            string[] days,
//            int totalHours,
//            Dictionary<string, Dictionary<string, HashSet<int>>> staffOcc,
//            Dictionary<string, Dictionary<string, HashSet<int>>> labOcc,
//            Dictionary<string, Dictionary<int, string>> timetableGrid)
//        {
//            // Create potential slots by relaxing some constraints
//            var potentialSlots = new List<(string day, int start)>();

//            foreach (var day in days)
//            {
//                for (int start = 1; start <= totalHours - task.Duration + 1; start++)
//                {
//                    // Relax constraints - only check basic time and grid availability
//                    bool canPlace = true;

//                    // Check time bounds
//                    if (start < 1 || start + task.Duration - 1 > totalHours)
//                        canPlace = false;

//                    // Enhanced lab-specific constraints
//                    if (task.IsLab)
//                    {
//                        if (task.Duration == 4)
//                        {
//                            // 4-hour labs: can start at hour 1 or 4
//                            if (!(start == 1 || start == 4))
//                                canPlace = false;
//                        }
//                        else if (task.Duration == 3)
//                        {
//                            // 3-hour labs: can start at hour 1, 4, or 5
//                            if (!(start == 1 || start == 4 || start == 5))
//                                canPlace = false;
//                        }
//                        else if (task.Duration == 2)
//                        {
//                            // 2-hour embedded labs: can start at hours 1-6
//                            if (start < 1 || start > 6)
//                                canPlace = false;
//                        }
//                    }

//                    if (canPlace)
//                        potentialSlots.Add((day, start));
//                }
//            }

//            if (potentialSlots.Count == 0)
//                return (false, ("", 0));

//            // Simple GA for single task
//            var population = new List<GAIndividual>();

//            // Create initial population
//            for (int i = 0; i < Math.Min(20, potentialSlots.Count); i++)
//            {
//                var individual = new GAIndividual();
//                individual.Assignments[task] = potentialSlots[rng.Next(potentialSlots.Count)];
//                individual.Fitness = EvaluateSingleTaskFitness(task, individual.Assignments[task], staffOcc, labOcc, timetableGrid);
//                population.Add(individual);
//            }

//            // Evolution
//            for (int gen = 0; gen < 50; gen++)
//            {
//                population = population.OrderBy(ind => ind.Fitness).ToList();

//                if (population[0].Fitness == 0)
//                {
//                    return (true, population[0].Assignments[task]);
//                }

//                // Create next generation
//                var nextGen = new List<GAIndividual> { population[0] }; // Elitism

//                while (nextGen.Count < population.Count)
//                {
//                    var parent = population[rng.Next(Math.Min(5, population.Count))]; // Tournament selection
//                    var child = parent.Clone();

//                    // Mutation
//                    if (rng.NextDouble() < 0.3)
//                    {
//                        child.Assignments[task] = potentialSlots[rng.Next(potentialSlots.Count)];
//                    }

//                    child.Fitness = EvaluateSingleTaskFitness(task, child.Assignments[task], staffOcc, labOcc, timetableGrid);
//                    nextGen.Add(child);
//                }

//                population = nextGen;
//            }

//            population = population.OrderBy(ind => ind.Fitness).ToList();

//            // Accept solution if fitness is reasonable (allow some conflicts)
//            if (population[0].Fitness < 5)
//            {
//                return (true, population[0].Assignments[task]);
//            }

//            return (false, ("", 0));
//        }

//        private (bool success, Dictionary<TaskUnit, (string day, int start)> assignments) TryGAFallbackComponent(
//            List<TaskUnit> component,
//            string[] days,
//            int totalHours,
//            Dictionary<string, Dictionary<string, HashSet<int>>> staffOcc,
//            Dictionary<string, Dictionary<string, HashSet<int>>> labOcc,
//            Dictionary<string, Dictionary<int, string>> timetableGrid)
//        {
//            if (component.Count > MAX_GENETIC_COMPONENT_SIZE)
//                return (false, null);

//            var population = new List<GAIndividual>();

//            // Create initial population
//            for (int i = 0; i < GA_POPULATION_SIZE; i++)
//            {
//                var individual = CreateRandomIndividual(component, days, totalHours);
//                if (individual != null)
//                {
//                    individual.Fitness = EvaluateComponentFitness(component, individual, staffOcc, labOcc, timetableGrid);
//                    population.Add(individual);
//                }
//            }

//            if (population.Count == 0)
//                return (false, null);

//            // Evolution
//            for (int gen = 0; gen < GA_MAX_GENERATIONS; gen++)
//            {
//                population = population.OrderBy(ind => ind.Fitness).ToList();

//                if (population[0].Fitness == 0)
//                {
//                    return (true, population[0].Assignments);
//                }

//                // Create next generation
//                var nextGen = new List<GAIndividual> { population[0], population[1] }; // Elitism

//                while (nextGen.Count < population.Count)
//                {
//                    var parent1 = TournamentSelection(population);
//                    var parent2 = TournamentSelection(population);
//                    var (child1, child2) = Crossover(parent1, parent2, component);

//                    Mutate(child1, component, days, totalHours);
//                    Mutate(child2, component, days, totalHours);

//                    child1.Fitness = EvaluateComponentFitness(component, child1, staffOcc, labOcc, timetableGrid);
//                    child2.Fitness = EvaluateComponentFitness(component, child2, staffOcc, labOcc, timetableGrid);

//                    nextGen.Add(child1);
//                    if (nextGen.Count < population.Count)
//                        nextGen.Add(child2);
//                }

//                population = nextGen;
//            }

//            population = population.OrderBy(ind => ind.Fitness).ToList();

//            // Accept solution if fitness is reasonable
//            if (population[0].Fitness < component.Count * 3)
//            {
//                return (true, population[0].Assignments);
//            }

//            return (false, null);
//        }

//        private GAIndividual CreateRandomIndividual(List<TaskUnit> tasks, string[] days, int totalHours)
//        {
//            var individual = new GAIndividual();

//            foreach (var task in tasks)
//            {
//                var validSlots = new List<(string day, int start)>();

//                foreach (var day in days)
//                {
//                    for (int start = 1; start <= totalHours - task.Duration + 1; start++)
//                    {
//                        bool canPlace = true;

//                        // Basic time bounds check
//                        if (start < 1 || start + task.Duration - 1 > totalHours)
//                            canPlace = false;

//                        // Enhanced lab-specific constraints
//                        if (task.IsLab)
//                        {
//                            if (task.Duration == 4)
//                            {
//                                // 4-hour labs: can start at hour 1 or 4
//                                if (!(start == 1 || start == 4))
//                                    canPlace = false;
//                            }
//                            else if (task.Duration == 3)
//                            {
//                                // 3-hour labs: can start at hour 1, 4, or 5
//                                if (!(start == 1 || start == 4 || start == 5))
//                                    canPlace = false;
//                            }
//                            else if (task.Duration == 2)
//                            {
//                                // 2-hour embedded labs: can start at hours 1-6
//                                if (start < 1 || start > 6)
//                                    canPlace = false;
//                            }
//                        }

//                        if (canPlace)
//                            validSlots.Add((day, start));
//                    }
//                }

//                if (validSlots.Count == 0)
//                    return null;

//                individual.Assignments[task] = validSlots[rng.Next(validSlots.Count)];
//            }

//            return individual;
//        }

//        private int EvaluateSingleTaskFitness(TaskUnit task, (string day, int start) slot,
//            Dictionary<string, Dictionary<string, HashSet<int>>> staffOcc,
//            Dictionary<string, Dictionary<string, HashSet<int>>> labOcc,
//            Dictionary<string, Dictionary<int, string>> timetableGrid)
//        {
//            int penalty = 0;
//            var (_, staffCode) = SplitStaff(task.StaffAssigned);

//            for (int h = slot.start; h < slot.start + task.Duration; h++)
//            {
//                // Grid conflict
//                if (timetableGrid.ContainsKey(slot.day) &&
//                    timetableGrid[slot.day].ContainsKey(h) &&
//                    timetableGrid[slot.day][h] != "---")
//                    penalty += 10;

//                // Staff conflict
//                if (staffOcc.ContainsKey(staffCode) &&
//                    staffOcc[staffCode].ContainsKey(slot.day) &&
//                    staffOcc[staffCode][slot.day].Contains(h))
//                    penalty += 5;

//                // Lab conflict - FIXED: Handle afternoon 4-hour labs correctly
//                if (task.IsLab && !string.IsNullOrEmpty(task.LabId) &&
//                    labOcc.ContainsKey(task.LabId) &&
//                    labOcc[task.LabId].ContainsKey(slot.day))
//                {
//                    // For 4-hour labs starting at hour 4 (afternoon), only check lab hours 5,6,7
//                    if (task.Duration == 4 && slot.start == 4)
//                    {
//                        if (h >= 5 && h <= 7 && labOcc[task.LabId][slot.day].Contains(h))
//                            penalty += 5;
//                    }
//                    else
//                    {
//                        // Normal lab conflict check for all other cases
//                        if (labOcc[task.LabId][slot.day].Contains(h))
//                            penalty += 5;
//                    }
//                }
//            }

//            // Enhanced lab time constraint penalty
//            if (task.IsLab)
//            {
//                if (task.Duration == 4)
//                {
//                    // 4-hour labs: penalty if not starting at hour 1 or 4
//                    if (!(slot.start == 1 || slot.start == 4))
//                        penalty += 20;
//                }
//                else if (task.Duration == 3)
//                {
//                    // 3-hour labs: penalty if not starting at hour 1, 4, or 5
//                    if (!(slot.start == 1 || slot.start == 4 || slot.start == 5))
//                        penalty += 15;
//                }
//                else if (task.Duration == 2)
//                {
//                    // 2-hour embedded labs: penalty if starting outside hours 1-6
//                    if (slot.start < 1 || slot.start > 6)
//                        penalty += 10;
//                }
//            }

//            return penalty;
//        }

//        private int EvaluateComponentFitness(List<TaskUnit> component, GAIndividual individual,
//            Dictionary<string, Dictionary<string, HashSet<int>>> staffOcc,
//            Dictionary<string, Dictionary<string, HashSet<int>>> labOcc,
//            Dictionary<string, Dictionary<int, string>> timetableGrid)
//        {
//            int penalty = 0;
//            var internalStaffSchedule = new Dictionary<string, Dictionary<string, HashSet<int>>>();
//            var internalLabSchedule = new Dictionary<string, Dictionary<string, HashSet<int>>>();
//            var dayDistribution = new Dictionary<string, int>(); // Track task distribution across days

//            foreach (var task in component)
//            {
//                if (!individual.Assignments.TryGetValue(task, out var slot))
//                    continue;

//                var (_, staffCode) = SplitStaff(task.StaffAssigned);

//                // Count tasks per day for balanced distribution
//                if (!dayDistribution.ContainsKey(slot.day))
//                    dayDistribution[slot.day] = 0;
//                dayDistribution[slot.day]++;

//                for (int h = slot.start; h < slot.start + task.Duration; h++)
//                {
//                    // External conflicts
//                    if (timetableGrid.ContainsKey(slot.day) &&
//                        timetableGrid[slot.day].ContainsKey(h) &&
//                        timetableGrid[slot.day][h] != "---")
//                        penalty += 10;

//                    if (staffOcc.ContainsKey(staffCode) &&
//                        staffOcc[staffCode].ContainsKey(slot.day) &&
//                        staffOcc[staffCode][slot.day].Contains(h))
//                        penalty += 8;

//                    // Lab conflict - FIXED: Handle afternoon 4-hour labs correctly  
//                    if (task.IsLab && !string.IsNullOrEmpty(task.LabId) &&
//                        labOcc.ContainsKey(task.LabId) &&
//                        labOcc[task.LabId].ContainsKey(slot.day))
//                    {
//                        // For 4-hour labs starting at hour 4 (afternoon), only check lab hours 5,6,7
//                        if (task.Duration == 4 && slot.start == 4)
//                        {
//                            if (h >= 5 && h <= 7 && labOcc[task.LabId][slot.day].Contains(h))
//                                penalty += 8;
//                        }
//                        else
//                        {
//                            // Normal lab conflict check for all other cases
//                            if (labOcc[task.LabId][slot.day].Contains(h))
//                                penalty += 8;
//                        }
//                    }

//                    // Internal conflicts
//                    if (!internalStaffSchedule.ContainsKey(staffCode))
//                        internalStaffSchedule[staffCode] = new Dictionary<string, HashSet<int>>();
//                    if (!internalStaffSchedule[staffCode].ContainsKey(slot.day))
//                        internalStaffSchedule[staffCode][slot.day] = new HashSet<int>();

//                    if (internalStaffSchedule[staffCode][slot.day].Contains(h))
//                        penalty += 15;
//                    else
//                        internalStaffSchedule[staffCode][slot.day].Add(h);

//                    if (task.IsLab && !string.IsNullOrEmpty(task.LabId))
//                    {
//                        if (!internalLabSchedule.ContainsKey(task.LabId))
//                            internalLabSchedule[task.LabId] = new Dictionary<string, HashSet<int>>();
//                        if (!internalLabSchedule[task.LabId].ContainsKey(slot.day))
//                            internalLabSchedule[task.LabId][slot.day] = new HashSet<int>();

//                        if (internalLabSchedule[task.LabId][slot.day].Contains(h))
//                            penalty += 15;
//                        else
//                            internalLabSchedule[task.LabId][slot.day].Add(h);
//                    }
//                }

//                // Enhanced lab time constraint penalty
//                if (task.IsLab)
//                {
//                    if (task.Duration == 4)
//                    {
//                        // 4-hour labs: penalty if not starting at hour 1 or 4
//                        if (!(slot.start == 1 || slot.start == 4))
//                            penalty += 20;
//                    }
//                    else if (task.Duration == 3)
//                    {
//                        // 3-hour labs: penalty if not starting at hour 1, 4, or 5
//                        if (!(slot.start == 1 || slot.start == 4 || slot.start == 5))
//                            penalty += 15;
//                    }
//                    else if (task.Duration == 2)
//                    {
//                        // 2-hour embedded labs: penalty if starting outside hours 1-6
//                        if (slot.start < 1 || slot.start > 6)
//                            penalty += 10;
//                    }
//                }
//            }

//            // Reward balanced distribution across days
//            if (dayDistribution.Count > 1)
//            {
//                var maxTasksPerDay = dayDistribution.Values.Max();
//                var minTasksPerDay = dayDistribution.Values.Min();
//                var imbalance = maxTasksPerDay - minTasksPerDay;
//                penalty += imbalance * 2; // Small penalty for imbalanced distribution
//            }

//            return penalty;
//        }

//        private GAIndividual TournamentSelection(List<GAIndividual> population)
//        {
//            int k = 3; // Tournament size
//            var selected = new List<GAIndividual>();

//            for (int i = 0; i < k; i++)
//            {
//                selected.Add(population[rng.Next(population.Count)]);
//            }

//            return selected.OrderBy(ind => ind.Fitness).First();
//        }

//        private (GAIndividual child1, GAIndividual child2) Crossover(GAIndividual parent1, GAIndividual parent2, List<TaskUnit> tasks)
//        {
//            var child1 = new GAIndividual();
//            var child2 = new GAIndividual();

//            int crossoverPoint = rng.Next(1, tasks.Count);

//            for (int i = 0; i < tasks.Count; i++)
//            {
//                var task = tasks[i];

//                if (i < crossoverPoint)
//                {
//                    if (parent1.Assignments.ContainsKey(task))
//                        child1.Assignments[task] = parent1.Assignments[task];
//                    if (parent2.Assignments.ContainsKey(task))
//                        child2.Assignments[task] = parent2.Assignments[task];
//                }
//                else
//                {
//                    if (parent2.Assignments.ContainsKey(task))
//                        child1.Assignments[task] = parent2.Assignments[task];
//                    if (parent1.Assignments.ContainsKey(task))
//                        child2.Assignments[task] = parent1.Assignments[task];
//                }
//            }

//            return (child1, child2);
//        }

//        private void Mutate(GAIndividual individual, List<TaskUnit> tasks, string[] days, int totalHours)
//        {
//            // Adaptive mutation rate based on component size
//            double adaptiveMutationRate = tasks.Count <= 5 ? 0.05 : GA_MUTATION_RATE;

//            foreach (var task in tasks)
//            {
//                if (rng.NextDouble() < adaptiveMutationRate)
//                {
//                    var validSlots = new List<(string day, int start)>();

//                    foreach (var day in days)
//                    {
//                        for (int start = 1; start <= totalHours - task.Duration + 1; start++)
//                        {
//                            bool canPlace = true;

//                            // Ensure 1-based hour indexing and duration constraints
//                            if (start < 1 || start + task.Duration - 1 > totalHours)
//                                canPlace = false;

//                            // Lab-specific constraints: 4-hour labs must start at 1 or 4
//                            if (task.IsLab && task.Duration == 4 && !(start == 1 || start == 4))
//                                canPlace = false;

//                            if (canPlace)
//                                validSlots.Add((day, start));
//                        }
//                    }

//                    if (validSlots.Count > 0)
//                    {
//                        individual.Assignments[task] = validSlots[rng.Next(validSlots.Count)];
//                    }
//                }
//            }
//        }

//        private async Task<(bool Succeeded, List<TaskUnit> Result)> RunGeneticAlgorithmAsync(
//            List<TaskUnit> tasksToAssign,
//            string[] days,
//            int hours,
//            Dictionary<string, Dictionary<string, HashSet<int>>> staffOcc,
//            Dictionary<string, Dictionary<string, HashSet<int>>> labOcc)
//        {
//            const int populationSize = 50;
//            const int maxGenerations = 150;
//            const double mutationRate = 0.15;

//            bool CanPlace(TaskUnit t, string day, int start)
//            {
//                if (start < 1 || start + t.Duration - 1 > hours)
//                    return false;

//                var (_, staffCode) = SplitStaff(t.StaffAssigned);
//                for (int h = start; h < start + t.Duration; h++)
//                {
//                    if (staffOcc.TryGetValue(staffCode, out var dayMap) && dayMap.TryGetValue(day, out var staffHours) && staffHours.Contains(h))
//                        return false;
//                    if (t.IsLab && !string.IsNullOrEmpty(t.LabId))
//                    {
//                        // FIXED: Handle afternoon 4-hour labs correctly
//                        if (t.Duration == 4 && start == 4)
//                        {
//                            // For afternoon 4-hour labs, only check lab hours 5,6,7
//                            if (h >= 5 && h <= 7 && labOcc.TryGetValue(t.LabId, out var labDayMap) && labDayMap.TryGetValue(day, out var labHours) && labHours.Contains(h))
//                                return false;
//                        }
//                        else
//                        {
//                            // Normal lab conflict check for all other cases
//                            if (labOcc.TryGetValue(t.LabId, out var labDayMap) && labDayMap.TryGetValue(day, out var labHours) && labHours.Contains(h))
//                                return false;
//                        }
//                    }
//                }
//                if (t.IsLab && t.Duration == 4 && !(start == 1 || start == 4))
//                    return false;

//                return true;
//            }

//            List<TaskUnit> CreateRandomIndividual()
//            {
//                var individual = new List<TaskUnit>();
//                foreach (var t in tasksToAssign)
//                {
//                    var validSlots = new List<(string day, int start)>();
//                    foreach (var day in days)
//                    {
//                        for (int start = 1; start <= hours - t.Duration + 1; start++)
//                        {
//                            if (CanPlace(t, day, start))
//                                validSlots.Add((day, start));
//                        }
//                    }
//                    if (validSlots.Count == 0)
//                        return null;
//                    var chosen = validSlots[rng.Next(validSlots.Count)];
//                    var copy = t.Clone();
//                    copy.Day = chosen.day;
//                    copy.StartHour = chosen.start;
//                    copy.IsPlaced = true;
//                    individual.Add(copy);
//                }
//                return individual;
//            }

//            int Fitness(List<TaskUnit> individual)
//            {
//                int penalty = 0;
//                var staffSchedule = new Dictionary<string, Dictionary<string, HashSet<int>>>();
//                var labSchedule = new Dictionary<string, Dictionary<string, HashSet<int>>>();

//                foreach (var t in tasksToAssign)
//                {
//                    var (_, staffCode) = SplitStaff(t.StaffAssigned);
//                    if (!staffSchedule.ContainsKey(staffCode))
//                        staffSchedule[staffCode] = days.ToDictionary(d => d, _ => new HashSet<int>());
//                    if (t.IsLab && !string.IsNullOrEmpty(t.LabId) && !labSchedule.ContainsKey(t.LabId))
//                        labSchedule[t.LabId] = days.ToDictionary(d => d, _ => new HashSet<int>());
//                }

//                foreach (var t in individual)
//                {
//                    var (_, staffCode) = SplitStaff(t.StaffAssigned);
//                    for (int h = t.StartHour; h < t.StartHour + t.Duration; h++)
//                    {
//                        if (staffOcc.TryGetValue(staffCode, out var dayMap) && dayMap.TryGetValue(t.Day, out var existingHours) && existingHours.Contains(h))
//                            penalty += 10;

//                        // FIXED: Handle afternoon 4-hour labs correctly
//                        if (t.IsLab && !string.IsNullOrEmpty(t.LabId) && labOcc.TryGetValue(t.LabId, out var labDayMap) && labDayMap.TryGetValue(t.Day, out var labExisting))
//                        {
//                            // For 4-hour labs starting at hour 4 (afternoon), only check lab hours 5,6,7
//                            if (t.Duration == 4 && t.StartHour == 4)
//                            {
//                                if (h >= 5 && h <= 7 && labExisting.Contains(h))
//                                    penalty += 10;
//                            }
//                            else
//                            {
//                                // Normal lab conflict check for all other cases
//                                if (labExisting.Contains(h))
//                                    penalty += 10;
//                            }
//                        }

//                        if (staffSchedule[staffCode][t.Day].Contains(h))
//                            penalty += 5;
//                        else
//                            staffSchedule[staffCode][t.Day].Add(h);

//                        if (t.IsLab && !string.IsNullOrEmpty(t.LabId))
//                        {
//                            if (labSchedule[t.LabId][t.Day].Contains(h))
//                                penalty += 5;
//                            else
//                                labSchedule[t.LabId][t.Day].Add(h);
//                        }
//                    }

//                    if (t.IsLab && t.Duration == 4 && !(t.StartHour == 1 || t.StartHour == 4))
//                        penalty += 20;
//                }
//                return penalty;
//            }

//            List<TaskUnit> TournamentSelection(List<List<TaskUnit>> population)
//            {
//                int k = 3;
//                var selected = new List<List<TaskUnit>>();
//                for (int i = 0; i < k; i++)
//                    selected.Add(population[rng.Next(population.Count)]);
//                return selected.OrderBy(ind => Fitness(ind)).First();
//            }

//            (List<TaskUnit>, List<TaskUnit>) Crossover(List<TaskUnit> parent1, List<TaskUnit> parent2)
//            {
//                int point = rng.Next(1, parent1.Count);
//                var child1 = new List<TaskUnit>();
//                var child2 = new List<TaskUnit>();
//                for (int i = 0; i < parent1.Count; i++)
//                {
//                    child1.Add(i < point ? parent1[i] : parent2[i]);
//                    child2.Add(i < point ? parent2[i] : parent1[i]);
//                }
//                return (child1, child2);
//            }

//            void Mutate(List<TaskUnit> individual)
//            {
//                for (int i = 0; i < individual.Count; i++)
//                {
//                    if (rng.NextDouble() < mutationRate)
//                    {
//                        var t = tasksToAssign[i];
//                        var validSlots = new List<(string day, int start)>();
//                        foreach (var day in days)
//                        {
//                            for (int start = 1; start <= hours - t.Duration + 1; start++)
//                            {
//                                if (CanPlace(t, day, start))
//                                    validSlots.Add((day, start));
//                            }
//                        }
//                        if (validSlots.Count > 0)
//                        {
//                            var chosen = validSlots[rng.Next(validSlots.Count)];
//                            individual[i].Day = chosen.day;
//                            individual[i].StartHour = chosen.start;
//                        }
//                    }
//                }
//            }

//            var population = new List<List<TaskUnit>>();
//            for (int i = 0; i < populationSize; i++)
//            {
//                var individual = CreateRandomIndividual();
//                if (individual != null)
//                    population.Add(individual);
//            }
//            if (population.Count == 0)
//                return (false, null);

//            for (int gen = 0; gen < maxGenerations; gen++)
//            {
//                population = population.OrderBy(ind => Fitness(ind)).ToList();
//                var best = population[0];
//                if (Fitness(best) == 0)
//                {
//                    return (true, best);
//                }
//                var nextGen = new List<List<TaskUnit>>
//                {
//                    population[0], population[1]
//                };
//                while (nextGen.Count < populationSize)
//                {
//                    var parent1 = TournamentSelection(population);
//                    var parent2 = TournamentSelection(population);
//                    var (child1, child2) = Crossover(parent1, parent2);
//                    Mutate(child1);
//                    Mutate(child2);
//                    nextGen.Add(child1);
//                    if (nextGen.Count < populationSize)
//                        nextGen.Add(child2);
//                }
//                population = nextGen;
//            }
//            population = population.OrderBy(ind => Fitness(ind)).ToList();
//            if (Fitness(population[0]) == 0)
//                return (true, population[0]);
//            return (false, null);
//        }

//        #endregion

//        #region Task Ordering and Selection Heuristics

//        private List<TaskUnit> OrderTasks(List<TaskUnit> tasks)
//        {
//            return tasks.OrderBy(t =>
//            {
//                // Priority: Lab > Embedded Lab > Theory
//                int typePriority = t.Kind switch
//                {
//                    "LAB4" => 0,
//                    "EMB_LAB2" => 1,
//                    "EMB_TH1" => 2,
//                    "TH1" => 3,
//                    _ => 4
//                };
//                return typePriority;
//            })
//            .ThenBy(t => t.DomainSlots.Count) // MRV (Most Constrained Variable) - deterministic ordering
//            .ThenByDescending(t => t.Conflicts.Count) // Degree heuristic
//            .ThenBy(t => t.SubjectCode) // Tie-breaker for deterministic ordering
//            .ToList();
//        }

//        private List<(string day, int start)> OrderDomainByLCV(TaskUnit task, List<TaskUnit> allTasks)
//        {
//            var slotConstraintCount = new Dictionary<(string day, int start), int>();

//            foreach (var slot in task.DomainSlots)
//            {
//                int constraintCount = 0;

//                foreach (var otherTask in allTasks)
//                {
//                    if (otherTask == task || otherTask.IsPlaced) continue;

//                    // Count how many slots this assignment would eliminate from other tasks
//                    foreach (var otherSlot in otherTask.DomainSlots)
//                    {
//                        if (WouldConflict(task, slot, otherTask, otherSlot))
//                        {
//                            constraintCount++;
//                        }
//                    }
//                }

//                slotConstraintCount[slot] = constraintCount;
//            }

//            // Least Constraining Value: choose slots that eliminate fewer options for other tasks
//            return slotConstraintCount.OrderBy(kvp => kvp.Value).Select(kvp => kvp.Key).ToList();
//        }

//        private bool WouldConflict(TaskUnit taskA, (string day, int start) slotA, TaskUnit taskB, (string day, int start) slotB)
//        {
//            if (slotA.day != slotB.day) return false;

//            int startA = slotA.start;
//            int endA = startA + taskA.Duration - 1;
//            int startB = slotB.start;
//            int endB = startB + taskB.Duration - 1;

//            if (!(endA >= startB && endB >= startA)) return false;

//            var (_, staffA) = SplitStaff(taskA.StaffAssigned);
//            var (_, staffB) = SplitStaff(taskB.StaffAssigned);

//            if (staffA == staffB) return true;

//            if (taskA.IsLab && taskB.IsLab &&
//                !string.IsNullOrEmpty(taskA.LabId) && !string.IsNullOrEmpty(taskB.LabId) &&
//                taskA.LabId == taskB.LabId)
//                return true;

//            return false;
//        }

//        #endregion

//        #region Main Backtracking Algorithm

//        private async Task<bool> RecursiveBacktracking(List<TaskUnit> tasks,
//            string[] days,
//            int totalHours,
//            Dictionary<string, Dictionary<string, HashSet<int>>> staffOcc,
//            Dictionary<string, Dictionary<string, HashSet<int>>> labOcc,
//            Dictionary<string, Dictionary<int, string>> timetableGrid,
//            int recursionDepth = 0)
//        {
//            recursionCounter++;
//            if (recursionDepth > MAX_RECURSION_DEPTH || recursionCounter > MAX_RECURSION_DEPTH)
//                return false;

//            // Check if all tasks are placed
//            if (tasks.All(t => t.IsPlaced))
//                return true;

//            // Select next task using simple MRV like original code for better accuracy
//            var selectedTask = tasks.Where(t => !t.IsPlaced).OrderBy(t => t.DomainSlots.Count).FirstOrDefault();
//            if (selectedTask == null)
//                return true;

//            // Use randomized domain order like original code
//            var orderedDomain = selectedTask.DomainSlots.OrderBy(_ => rng.Next()).ToList();

//            // Store domain snapshot before attempting assignments
//            var domainSnapshot = tasks.ToDictionary(t => t, t => t.DomainSlots.ToList());

//            foreach (var slot in orderedDomain)
//            {
//                // Validate slot is within bounds and respects constraints
//                if (slot.start < 1 || slot.start + selectedTask.Duration - 1 > totalHours)
//                    continue;

//                // Check if slot is still valid
//                if (!IsFreeAndNoConflict(selectedTask, slot.day, slot.start, staffOcc, labOcc, timetableGrid, totalHours))
//                    continue;

//                // Make assignment
//                AssignTask(selectedTask, slot, staffOcc, labOcc, timetableGrid);

//                // Propagate constraints
//                if (!PropagateConstraints(tasks, selectedTask, slot))
//                {
//                    UnassignTask(selectedTask, staffOcc, labOcc, timetableGrid);
//                    // Restore domains after failed propagation
//                    foreach (var kvp in domainSnapshot)
//                        kvp.Key.DomainSlots = kvp.Value;
//                    continue;
//                }

//                // Recursive call
//                if (await RecursiveBacktracking(tasks, days, totalHours, staffOcc, labOcc, timetableGrid, recursionDepth + 1))
//                    return true;

//                // Backtrack - unassign task
//                UnassignTask(selectedTask, staffOcc, labOcc, timetableGrid);

//                // Enhanced conflict handling with rescheduling attempt first
//                var conflictComponent = GetConflictComponent(selectedTask);
//                if (conflictComponent.Count > 0 && recursionCounter <= MAX_RECURSION_DEPTH)
//                {
//                    // First try rescheduling approach for small conflicts
//                    if (conflictComponent.Count <= 2)
//                    {
//                        Console.WriteLine($"Attempting rescheduling for small conflict component of size {conflictComponent.Count}");
//                        if (TryReschedulingToMakeRoom(selectedTask, tasks, days, totalHours, staffOcc, labOcc, timetableGrid))
//                        {
//                            // Try again after rescheduling
//                        }
//                    }
//                }
//                {
//                    // Reset conflict component and try GA fallback like original
//                    foreach (var conflictTask in conflictComponent)
//                    {
//                        if (conflictTask.IsPlaced)
//                        {
//                            UnassignTask(conflictTask, staffOcc, labOcc, timetableGrid);
//                        }
//                        conflictTask.IsPlaced = false;
//                        conflictTask.DomainSlots.Clear();

//                        // Recompute domain like original code
//                        foreach (var day in days)
//                        {
//                            for (int start = 1; start <= totalHours - conflictTask.Duration + 1; start++)
//                            {
//                                if (IsFreeAndNoConflict(conflictTask, day, start, staffOcc, labOcc, timetableGrid, totalHours))
//                                {
//                                    conflictTask.DomainSlots.Add((day, start));
//                                }
//                            }
//                        }

//                        if (conflictTask.DomainSlots.Count == 0)
//                        {
//                            // Restore domains and continue with next slot
//                            foreach (var kvp in domainSnapshot)
//                                kvp.Key.DomainSlots = kvp.Value;
//                            break;
//                        }
//                    }

//                    // Try GA fallback like original code
//                    if (conflictComponent.Count > 0)
//                    {
//                        var gaResult = await RunGeneticAlgorithmAsync(conflictComponent, days, totalHours, staffOcc, labOcc);
//                        if (gaResult.Succeeded)
//                        {
//                            // Apply GA solution like original code
//                            foreach (var gaTask in gaResult.Result)
//                            {
//                                var originalTask = conflictComponent.FirstOrDefault(t => t.SubjectCode == gaTask.SubjectCode && t.Kind == gaTask.Kind);
//                                if (originalTask != null)
//                                {
//                                    originalTask.DomainSlots.Clear();
//                                    originalTask.DomainSlots.Add((gaTask.Day, gaTask.StartHour));
//                                    AssignTask(originalTask, (gaTask.Day, gaTask.StartHour), staffOcc, labOcc, timetableGrid);
//                                }
//                            }

//                            // Rebuild conflict graph and continue like original
//                            BuildConflictGraph(tasks);
//                            if (await RecursiveBacktracking(tasks, days, totalHours, staffOcc, labOcc, timetableGrid, recursionDepth + 1))
//                                return true;

//                            // Cleanup if GA solution didn't work
//                            foreach (var conflictTask in conflictComponent)
//                            {
//                                UnassignTask(conflictTask, staffOcc, labOcc, timetableGrid);
//                            }
//                        }
//                        else
//                        {
//                            // GA failed, clean up conflict tasks like original
//                            foreach (var conflictTask in conflictComponent)
//                            {
//                                UnassignTask(conflictTask, staffOcc, labOcc, timetableGrid);
//                            }
//                        }
//                    }
//                }

//                // Restore domains for next iteration
//                foreach (var kvp in domainSnapshot)
//                    kvp.Key.DomainSlots = kvp.Value;
//            }

//            return false;
//        }

//        #endregion

//        #region Main API Endpoint

//        [HttpPost("generateCrossDepartmentTimetableBacktracking")]
//        public async Task<IActionResult> GenerateEnhancedTimetable([FromBody] TimetableRequest request)
//        {
//            if (request == null || request.Subjects == null || request.Subjects.Count == 0)
//                return BadRequest(new { message = "❌ Request or subjects missing." });

//            var cs = _configuration.GetConnectionString("DefaultConnection");
//            await using var conn = new NpgsqlConnection(cs);
//            await conn.OpenAsync();

//            string[] DAYS = new[] { "Mon", "Tue", "Wed", "Thu", "Fri" };
//            int HOURS = Math.Max(1, request.TotalHoursPerDay);

//            // Initialize data structures
//            var timetableGrid = DAYS.ToDictionary(d => d, d => Enumerable.Range(1, HOURS).ToDictionary(h => h, _ => "---"));
//            var staffOcc = new Dictionary<string, Dictionary<string, HashSet<int>>>();
//            var labOcc = new Dictionary<string, Dictionary<string, HashSet<int>>>();

//            // Load existing occupancy from database
//            await LoadExistingOccupancy(conn, staffOcc, labOcc, DAYS);

//            // Filter subjects with assigned staff like original code
//            var validSubjects = request.Subjects?.Where(s => !string.IsNullOrWhiteSpace(s.StaffAssigned)).ToList();
//            if (validSubjects == null || validSubjects.Count == 0)
//                return BadRequest(new { message = "❌ No valid subjects with assigned staff found.", receivedPayload = request });

//            // Create task units from valid subjects
//            var tasks = CreateTaskUnits(validSubjects);
//            if (tasks.Count == 0)
//                return Ok(new { message = "❌ No valid tasks created from subjects.", receivedPayload = request });

//            // Shuffle tasks like original code for randomization
//            Shuffle(tasks);

//            // Reset recursion counter for this generation attempt
//            recursionCounter = 0;

//            try
//            {
//                // Enhanced domain assignment with rescheduling
//                if (!AssignDomains(tasks, DAYS, HOURS, staffOcc, labOcc, timetableGrid))
//                {
//                    return Ok(new { message = $"❌ Unable to find or create available slots for some tasks even after rescheduling attempts.", receivedPayload = request });
//                }

//                // Build conflict graph like original code
//                BuildConflictGraph(tasks);

//                // Main backtracking algorithm with integrated GA fallback
//                var solved = await RecursiveBacktracking(tasks, DAYS, HOURS, staffOcc, labOcc, timetableGrid);

//                if (!solved)
//                {
//                    return Ok(new { message = "❌ Unsolvable conflict detected", receivedPayload = request });
//                }

//                // Validate solution with both methods
//                if (!ValidateSolution(tasks, HOURS) || !ValidateFinalTimetable(tasks, HOURS))
//                {
//                    return Ok(new { message = "❌ Timetable validation failed after generation.", receivedPayload = request });
//                }

//                // Save to database
//                await SaveTimetableToDatabase(conn, request, tasks);

//                return Ok(new
//                {
//                    message = "✅ Conflict-free timetable generated successfully with enhanced algorithms.",
//                    timetable = timetableGrid.Select(kvp => new { Day = kvp.Key, Slots = kvp.Value }),
//                    usedLabIds = tasks.Where(t => t.IsLab && !string.IsNullOrEmpty(t.LabId)).Select(t => t.LabId).Distinct(),
//                    taskCount = tasks.Count,
//                    placedTasks = tasks.Count(t => t.IsPlaced),
//                    receivedPayload = request
//                });
//            }
//            catch (Exception ex)
//            {
//                return StatusCode(500, new { message = $"❌ Error during timetable generation: {ex.Message}" });
//            }
//        }

//        #endregion

//        #region Helper Methods for Database and Validation

//        private void EnsureDayMap<TKey>(Dictionary<TKey, Dictionary<string, HashSet<int>>> map, TKey key, string[] days)
//        {
//            if (!map.ContainsKey(key))
//                map[key] = days.ToDictionary(d => d, _ => new HashSet<int>());
//        }

//        private async Task LoadExistingOccupancy(NpgsqlConnection conn,
//            Dictionary<string, Dictionary<string, HashSet<int>>> staffOcc,
//            Dictionary<string, Dictionary<string, HashSet<int>>> labOcc,
//            string[] days)
//        {
//            // Load staff occupancy from existing classtimetable
//            await using (var cmd = new NpgsqlCommand("SELECT staff_code, day, hour FROM classtimetable", conn))
//            await using (var reader = await cmd.ExecuteReaderAsync())
//            {
//                while (await reader.ReadAsync())
//                {
//                    var staffCode = reader["staff_code"] as string ?? "---";
//                    var day = reader["day"] as string ?? "Mon";
//                    var hour = (int)reader["hour"];

//                    EnsureDayMap(staffOcc, staffCode, days);
//                    staffOcc[staffCode][day].Add(hour);
//                }
//            }

//            // Load lab occupancy from existing labtimetable
//            await using (var cmd = new NpgsqlCommand("SELECT lab_id, day, hour FROM labtimetable", conn))
//            await using (var reader = await cmd.ExecuteReaderAsync())
//            {
//                while (await reader.ReadAsync())
//                {
//                    var labId = reader["lab_id"] as string ?? "---";
//                    var day = reader["day"] as string ?? "Mon";
//                    var hour = (int)reader["hour"];

//                    EnsureDayMap(labOcc, labId, days);
//                    labOcc[labId][day].Add(hour);
//                }
//            }
//        }

//        private List<TaskUnit> CreateTaskUnits(List<SubjectDto> subjects)
//        {
//            var tasks = new List<TaskUnit>();

//            foreach (var subject in subjects.Where(s => !string.IsNullOrWhiteSpace(s.StaffAssigned)))
//            {
//                var type = (subject.SubjectType ?? "theory").Trim().ToLowerInvariant();

//                switch (type)
//                {
//                    case "lab":
//                        tasks.Add(new TaskUnit
//                        {
//                            SubjectCode = subject.SubjectCode ?? "---",
//                            SubjectName = subject.SubjectName ?? "---",
//                            StaffAssigned = subject.StaffAssigned,
//                            LabId = subject.LabId,
//                            IsLab = true,
//                            Duration = 4,
//                            Kind = "LAB4"
//                        });
//                        break;

//                    case "embedded":
//                        tasks.Add(new TaskUnit
//                        {
//                            SubjectCode = subject.SubjectCode ?? "---",
//                            SubjectName = subject.SubjectName ?? "---",
//                            StaffAssigned = subject.StaffAssigned,
//                            LabId = subject.LabId,
//                            IsLab = true,
//                            Duration = 2,
//                            Kind = "EMB_LAB2"
//                        });

//                        for (int i = 0; i < 2; i++)
//                        {
//                            tasks.Add(new TaskUnit
//                            {
//                                SubjectCode = subject.SubjectCode ?? "---",
//                                SubjectName = subject.SubjectName ?? "---",
//                                StaffAssigned = subject.StaffAssigned,
//                                IsLab = false,
//                                Duration = 1,
//                                Kind = "EMB_TH1"
//                            });
//                        }
//                        break;

//                    default: // theory
//                        int creditHours = Math.Max(0, subject.Credit);
//                        for (int i = 0; i < creditHours; i++)
//                        {
//                            tasks.Add(new TaskUnit
//                            {
//                                SubjectCode = subject.SubjectCode ?? "---",
//                                SubjectName = subject.SubjectName ?? "---",
//                                StaffAssigned = subject.StaffAssigned,
//                                IsLab = false,
//                                Duration = 1,
//                                Kind = "TH1"
//                            });
//                        }
//                        break;
//                }
//            }

//            return tasks;
//        }

//        private bool ValidateSolution(List<TaskUnit> tasks, int totalHours)
//        {
//            var staffSchedule = new Dictionary<string, Dictionary<string, HashSet<int>>>();
//            var labSchedule = new Dictionary<string, Dictionary<string, HashSet<int>>>();
//            var globalGrid = new Dictionary<string, Dictionary<int, string>>();

//            // Initialize global grid for overlap detection
//            string[] DAYS = { "Mon", "Tue", "Wed", "Thu", "Fri" };
//            foreach (var day in DAYS)
//            {
//                globalGrid[day] = new Dictionary<int, string>();
//                for (int h = 1; h <= totalHours; h++)
//                {
//                    globalGrid[day][h] = "---";
//                }
//            }

//            foreach (var task in tasks)
//            {
//                if (!task.IsPlaced)
//                {
//                    Console.WriteLine($"Validation failed: Task {task.SubjectCode} is not placed");
//                    return false;
//                }

//                var (_, staffCode) = SplitStaff(task.StaffAssigned);

//                // Validate basic constraints
//                if (string.IsNullOrEmpty(task.Day) || task.StartHour < 1)
//                {
//                    Console.WriteLine($"Validation failed: Task {task.SubjectCode} has invalid assignment");
//                    return false;
//                }

//                // Initialize staff schedule tracking
//                if (!staffSchedule.ContainsKey(staffCode))
//                    staffSchedule[staffCode] = new Dictionary<string, HashSet<int>>();
//                if (!staffSchedule[staffCode].ContainsKey(task.Day))
//                    staffSchedule[staffCode][task.Day] = new HashSet<int>();

//                // Initialize lab schedule tracking
//                if (task.IsLab && !string.IsNullOrEmpty(task.LabId))
//                {
//                    if (!labSchedule.ContainsKey(task.LabId))
//                        labSchedule[task.LabId] = new Dictionary<string, HashSet<int>>();
//                    if (!labSchedule[task.LabId].ContainsKey(task.Day))
//                        labSchedule[task.LabId][task.Day] = new HashSet<int>();
//                }

//                // Check each hour of the task
//                for (int h = task.StartHour; h < task.StartHour + task.Duration; h++)
//                {
//                    // Validate time bounds (1-based indexing)
//                    if (h < 1 || h > totalHours)
//                    {
//                        Console.WriteLine($"Validation failed: Task {task.SubjectCode} hour {h} is out of bounds");
//                        return false;
//                    }

//                    // Check global grid conflicts
//                    if (globalGrid[task.Day][h] != "---")
//                    {
//                        Console.WriteLine($"Validation failed: Global conflict at {task.Day} hour {h}");
//                        return false;
//                    }
//                    globalGrid[task.Day][h] = task.SubjectCode;

//                    // Check staff conflicts
//                    if (staffSchedule[staffCode][task.Day].Contains(h))
//                    {
//                        Console.WriteLine($"Validation failed: Staff {staffCode} conflict at {task.Day} hour {h}");
//                        return false;
//                    }
//                    staffSchedule[staffCode][task.Day].Add(h);

//                    // Check lab conflicts
//                    if (task.IsLab && !string.IsNullOrEmpty(task.LabId))
//                    {
//                        if (labSchedule[task.LabId][task.Day].Contains(h))
//                        {
//                            Console.WriteLine($"Validation failed: Lab {task.LabId} conflict at {task.Day} hour {h}");
//                            return false;
//                        }
//                        labSchedule[task.LabId][task.Day].Add(h);
//                    }
//                }

//                // Validate lab-specific constraints: 4-hour labs must start at hour 1 or 4
//                if (task.IsLab && task.Duration == 4 && !(task.StartHour == 1 || task.StartHour == 4))
//                {
//                    Console.WriteLine($"Validation failed: Lab {task.SubjectCode} starts at invalid hour {task.StartHour}");
//                    return false;
//                }

//                // Validate duration constraints
//                if (task.StartHour + task.Duration - 1 > totalHours)
//                {
//                    Console.WriteLine($"Validation failed: Task {task.SubjectCode} duration exceeds day limits");
//                    return false;
//                }
//            }

//            Console.WriteLine("Solution validation passed successfully");
//            return true;
//        }

//        private bool ValidateFinalTimetable(List<TaskUnit> tasks, int totalHours)
//        {
//            var staffSchedule = new Dictionary<string, Dictionary<string, HashSet<int>>>();
//            var labSchedule = new Dictionary<string, Dictionary<string, HashSet<int>>>();

//            foreach (var task in tasks)
//            {
//                if (!task.IsPlaced)
//                {
//                    Console.WriteLine($"❌ Task {task.SubjectCode} is not placed");
//                    return false;
//                }

//                var (_, staffCode) = SplitStaff(task.StaffAssigned);

//                if (!staffSchedule.ContainsKey(staffCode))
//                    staffSchedule[staffCode] = new Dictionary<string, HashSet<int>>();
//                if (!staffSchedule[staffCode].ContainsKey(task.Day))
//                    staffSchedule[staffCode][task.Day] = new HashSet<int>();

//                if (task.IsLab && !string.IsNullOrEmpty(task.LabId))
//                {
//                    if (!labSchedule.ContainsKey(task.LabId))
//                        labSchedule[task.LabId] = new Dictionary<string, HashSet<int>>();
//                    if (!labSchedule[task.LabId].ContainsKey(task.Day))
//                        labSchedule[task.LabId][task.Day] = new HashSet<int>();
//                }

//                // Validate hour bounds and conflicts
//                for (int h = task.StartHour; h < task.StartHour + task.Duration; h++)
//                {
//                    if (h < 1 || h > totalHours)
//                    {
//                        Console.WriteLine($"❌ Task {task.SubjectCode} has invalid hours: {task.StartHour}-{task.StartHour + task.Duration - 1}");
//                        return false;
//                    }

//                    // Check staff conflicts within this generation
//                    if (staffSchedule[staffCode][task.Day].Contains(h))
//                    {
//                        Console.WriteLine($"❌ Staff conflict detected for {staffCode} on {task.Day} at hour {h}");
//                        return false;
//                    }
//                    staffSchedule[staffCode][task.Day].Add(h);

//                    // Check lab conflicts within this generation
//                    if (task.IsLab && !string.IsNullOrEmpty(task.LabId))
//                    {
//                        if (labSchedule[task.LabId][task.Day].Contains(h))
//                        {
//                            Console.WriteLine($"❌ Lab conflict detected for {task.LabId} on {task.Day} at hour {h}");
//                            return false;
//                        }
//                        labSchedule[task.LabId][task.Day].Add(h);
//                    }
//                }

//                // Validate lab-specific constraints
//                if (task.IsLab && task.Duration == 4 && !(task.StartHour == 1 || task.StartHour == 4))
//                {
//                    Console.WriteLine($"❌ 4-hour lab {task.SubjectCode} has invalid start hour: {task.StartHour}");
//                    return false;
//                }
//            }

//            // Check for global slot conflicts (no two tasks at same time slot)
//            var globalSlotMap = new Dictionary<(string Day, int Hour), TaskUnit>();
//            foreach (var task in tasks)
//            {
//                for (int h = task.StartHour; h < task.StartHour + task.Duration; h++)
//                {
//                    var key = (task.Day, h);
//                    if (globalSlotMap.ContainsKey(key))
//                    {
//                        Console.WriteLine($"❌ Global slot conflict at {task.Day} hour {h} between {globalSlotMap[key].SubjectCode} and {task.SubjectCode}");
//                        return false;
//                    }
//                    globalSlotMap[key] = task;
//                }
//            }

//            return true;
//        }

//        private async Task SaveTimetableToDatabase(NpgsqlConnection conn, TimetableRequest request, List<TaskUnit> tasks)
//        {
//            await using var transaction = await conn.BeginTransactionAsync();

//            // Delete existing entries for this department/year/semester/section
//            await using (var deleteCmd = new NpgsqlCommand(@"
//                DELETE FROM classtimetable 
//                WHERE department_id = @dept AND year = @year AND semester = @sem AND section = @section;
//                DELETE FROM labtimetable 
//                WHERE department = @dept AND year = @year AND semester = @sem AND section = @section;",
//                conn, transaction))
//            {
//                deleteCmd.Parameters.AddWithValue("dept", request.Department ?? "---");
//                deleteCmd.Parameters.AddWithValue("year", request.Year ?? "---");
//                deleteCmd.Parameters.AddWithValue("sem", request.Semester ?? "---");
//                deleteCmd.Parameters.AddWithValue("section", request.Section ?? "---");
//                await deleteCmd.ExecuteNonQueryAsync();
//            }

//            // Insert new timetable entries
//            foreach (var task in tasks.Where(t => t.IsPlaced))
//            {
//                var (staffName, staffCode) = SplitStaff(task.StaffAssigned);

//                // Insert into classtimetable for all hours
//                for (int h = task.StartHour; h < task.StartHour + task.Duration; h++)
//                {
//                    await using (var classCmd = new NpgsqlCommand(@"
//                        INSERT INTO classtimetable 
//                        (staff_name, staff_code, department_id, year, semester, section, day, hour, subject_code, subject_name)
//                        VALUES (@staff_name, @staff_code, @dept, @year, @sem, @section, @day, @hour, @sub_code, @sub_name)",
//                        conn, transaction))
//                    {
//                        classCmd.Parameters.AddWithValue("staff_name", staffName);
//                        classCmd.Parameters.AddWithValue("staff_code", staffCode);
//                        classCmd.Parameters.AddWithValue("dept", request.Department ?? "---");
//                        classCmd.Parameters.AddWithValue("year", request.Year ?? "---");
//                        classCmd.Parameters.AddWithValue("sem", request.Semester ?? "---");
//                        classCmd.Parameters.AddWithValue("section", request.Section ?? "---");
//                        classCmd.Parameters.AddWithValue("day", task.Day);
//                        classCmd.Parameters.AddWithValue("hour", h);
//                        classCmd.Parameters.AddWithValue("sub_code", task.SubjectCode);
//                        classCmd.Parameters.AddWithValue("sub_name", task.SubjectName);
//                        await classCmd.ExecuteNonQueryAsync();
//                    }
//                }

//                // Insert into labtimetable for lab subjects with special hour mapping
//                if (task.IsLab && !string.IsNullOrEmpty(task.LabId))
//                {
//                    // For 4-hour labs: if starting at hour 4 (afternoon), lab timetable shows 5,6,7
//                    // For other start times, lab timetable matches class timetable
//                    int labStartHour = task.StartHour == 4 ? 5 : task.StartHour;

//                    for (int h = labStartHour; h < task.StartHour + task.Duration; h++)
//                    {
//                        await using (var labCmd = new NpgsqlCommand(@"
//                            INSERT INTO labtimetable 
//                            (lab_id, subject_code, subject_name, staff_name, department, year, semester, section, day, hour)
//                            VALUES (@lab_id, @sub_code, @sub_name, @staff_name, @dept, @year, @sem, @section, @day, @hour)",
//                            conn, transaction))
//                        {
//                            labCmd.Parameters.AddWithValue("lab_id", task.LabId);
//                            labCmd.Parameters.AddWithValue("sub_code", task.SubjectCode);
//                            labCmd.Parameters.AddWithValue("sub_name", task.SubjectName);
//                            labCmd.Parameters.AddWithValue("staff_name", staffName);
//                            labCmd.Parameters.AddWithValue("dept", request.Department ?? "---");
//                            labCmd.Parameters.AddWithValue("year", request.Year ?? "---");
//                            labCmd.Parameters.AddWithValue("sem", request.Semester ?? "---");
//                            labCmd.Parameters.AddWithValue("section", request.Section ?? "---");
//                            labCmd.Parameters.AddWithValue("day", task.Day);
//                            labCmd.Parameters.AddWithValue("hour", h);
//                            await labCmd.ExecuteNonQueryAsync();
//                        }
//                    }
//                }
//            }

//            await transaction.CommitAsync();
//        }

//        #endregion
//    }
//}




using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Configuration;
using Npgsql;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using System.Text;

namespace Timetablegenerator.Controllers
{
    /// <summary>
    /// Enhanced Timetable Controller with advanced constraint satisfaction and genetic algorithm fallback.
    /// 
    /// Lab Scheduling Constraints:
    /// 1. 4-hour labs (LAB4): Must start at hour 1 (morning) or hour 4 (afternoon)
    ///    - Morning lab (1-4): Class timetable shows 1,2,3,4; Lab timetable shows 1,2,3,4
    ///    - Afternoon lab (4-7): Class timetable shows 4,5,6,7; Lab timetable shows 5,6,7 (hour 4 controlled by class)
    /// 2. Embedded subjects: Create 1 lab task (2 hours) + 2 theory tasks (1 hour each)
    /// 3. Regular theory: Create tasks based on credit hours (1 hour per task)
    /// </summary>
    [ApiController]
    [Route("api/[controller]")]
    public class EnhancedTimetableController : ControllerBase
    {
        private readonly IConfiguration _configuration;
        private static readonly Random rng = new Random();
        private const int MAX_RECURSION_DEPTH = 900;
        private int recursionCounter = 0;
        private const int MAX_GENETIC_COMPONENT_SIZE = 10; // Reduced for better performance
        private const int GA_MAX_GENERATIONS = 100;
        private const int GA_POPULATION_SIZE = 50;
        private const double GA_MUTATION_RATE = 0.07; // Reduced for better convergence

        public EnhancedTimetableController(IConfiguration configuration)
        {
            _configuration = configuration;
        }

        #region DTOs and Data Structures

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
            public string SubjectType { get; set; } // "theory", "lab", "embedded"
            public int Credit { get; set; }
            public string StaffAssigned { get; set; }
            public string LabId { get; set; }
        }

        public class TaskUnit
        {
            public string SubjectCode { get; set; }
            public string SubjectName { get; set; }
            public string StaffAssigned { get; set; }
            public string LabId { get; set; }
            public bool IsLab { get; set; }
            public int Duration { get; set; }
            public string Kind { get; set; } // "LAB4", "EMB_LAB2", "EMB_TH1", "TH1"

            // Assignment state
            public string Day { get; set; }
            public int StartHour { get; set; }
            public bool IsPlaced { get; set; } = false;

            // Constraint satisfaction properties
            public List<(string day, int start)> DomainSlots { get; set; } = new();
            public List<TaskUnit> Conflicts { get; set; } = new();
            public (string day, int start) AssignedSlot => IsPlaced ? (Day, StartHour) : ("", 0);

            // Compatibility property for original code patterns
            public List<(string day, int start)> Domain
            {
                get => DomainSlots;
                set => DomainSlots = value;
            }

            public TaskUnit Clone()
            {
                return new TaskUnit
                {
                    SubjectCode = this.SubjectCode,
                    SubjectName = this.SubjectName,
                    StaffAssigned = this.StaffAssigned,
                    LabId = this.LabId,
                    IsLab = this.IsLab,
                    Duration = this.Duration,
                    Kind = this.Kind,
                    Day = this.Day,
                    StartHour = this.StartHour,
                    IsPlaced = this.IsPlaced,
                    DomainSlots = new List<(string, int)>(this.DomainSlots)
                };
            }
        }

        public class GAIndividual
        {
            public Dictionary<TaskUnit, (string day, int start)> Assignments { get; set; } = new();
            public int Fitness { get; set; } = int.MaxValue;

            public GAIndividual Clone()
            {
                return new GAIndividual
                {
                    Assignments = new Dictionary<TaskUnit, (string, int)>(this.Assignments),
                    Fitness = this.Fitness
                };
            }
        }

        #endregion

        #region Helper Methods

        private void Shuffle<T>(IList<T> list)
        {
            int n = list.Count;
            while (n > 1)
            {
                n--;
                int k = rng.Next(n + 1);
                (list[k], list[n]) = (list[n], list[k]);
            }
        }

        private (string staffName, string staffCode) SplitStaff(string staffAssigned)
        {
            if (string.IsNullOrWhiteSpace(staffAssigned))
                return ("---", "---");

            var name = staffAssigned;
            var code = staffAssigned;

            if (staffAssigned.Contains("("))
            {
                var parts = staffAssigned.Split('(', StringSplitOptions.TrimEntries);
                name = parts[0].Trim();
                code = parts.Length > 1 ? parts[1].Replace(")", "").Trim() : "---";
            }

            if (string.IsNullOrEmpty(code))
                code = "---";

            return (name, code);
        }

        private bool IsFreeAndNoConflict(TaskUnit task, string day, int start,
            Dictionary<string, Dictionary<string, HashSet<int>>> staffOcc,
            Dictionary<string, Dictionary<string, HashSet<int>>> labOcc,
            Dictionary<string, Dictionary<int, string>> timetableGrid,
            int totalHours)
        {
            // Validate input parameters
            if (string.IsNullOrEmpty(day) || start < 1 || task == null)
                return false;

            // Check time bounds with 1-based indexing
            if (start < 1 || start + task.Duration - 1 > totalHours)
                return false;

            // Enhanced lab-specific time constraints based on duration
            if (task.IsLab)
            {
                if (task.Duration == 4)
                {
                    // 4-hour labs: can start at hour 1 (morning: 1-4) or hour 4 (afternoon: 4-7)
                    if (!(start == 1 || start == 4))
                        return false;
                }
                else if (task.Duration == 3)
                {
                    // 3-hour labs: can start at hour 1 (1-3), hour 4 (4-6), or hour 5 (5-7)
                    if (!(start == 1 || start == 4 || start == 5))
                        return false;
                }
                else if (task.Duration == 2)
                {
                    // 2-hour embedded labs: more flexible, can start at hours 1-6
                    if (start < 1 || start > 6)
                        return false;
                }
            }

            // Check grid availability for all hours of the task
            if (!timetableGrid.ContainsKey(day))
                return false;

            for (int h = start; h < start + task.Duration; h++)
            {
                if (!timetableGrid[day].ContainsKey(h) || timetableGrid[day][h] != "---")
                    return false;
            }

            // Check staff conflicts
            var (_, staffCode) = SplitStaff(task.StaffAssigned);
            if (!string.IsNullOrEmpty(staffCode))
            {
                for (int h = start; h < start + task.Duration; h++)
                {
                    if (staffOcc.TryGetValue(staffCode, out var dayMap) &&
                        dayMap.TryGetValue(day, out var staffHours) &&
                        staffHours.Contains(h))
                        return false;
                }
            }

            // Check lab conflicts - FIXED: Handle afternoon 4-hour labs correctly
            if (task.IsLab && !string.IsNullOrEmpty(task.LabId))
            {
                // For 4-hour labs starting at hour 4 (afternoon), only check lab hours 5,6,7
                // Hour 4 is controlled by class timetable, not lab timetable
                if (task.Duration == 4 && start == 4)
                {
                    Console.WriteLine($"🔍 Checking afternoon 4-hour lab {task.SubjectCode} at {day} hour 4: checking lab hours 5,6,7 only");
                    // Afternoon 4-hour lab: check lab occupancy for hours 5,6,7 only
                    for (int h = 5; h <= 7; h++)
                    {
                        if (labOcc.TryGetValue(task.LabId, out var labDayMap) &&
                            labDayMap.TryGetValue(day, out var labHours) &&
                            labHours.Contains(h))
                        {
                            Console.WriteLine($"❌ Afternoon lab conflict: {task.SubjectCode} blocked by lab hour {h} in {task.LabId}");
                            return false;
                        }
                    }
                    Console.WriteLine($"✅ Afternoon lab slot {day} hour 4 is free for {task.SubjectCode}");
                }
                else
                {
                    // Normal lab conflict check for all other cases
                    for (int h = start; h < start + task.Duration; h++)
                    {
                        if (labOcc.TryGetValue(task.LabId, out var labDayMap) &&
                            labDayMap.TryGetValue(day, out var labHours) &&
                            labHours.Contains(h))
                            return false;
                    }
                }
            }

            return true;
        }

        private void AssignTask(TaskUnit task, (string day, int start) slot,
            Dictionary<string, Dictionary<string, HashSet<int>>> staffOcc,
            Dictionary<string, Dictionary<string, HashSet<int>>> labOcc,
            Dictionary<string, Dictionary<int, string>> timetableGrid)
        {
            var (day, start) = slot;
            var (_, staffCode) = SplitStaff(task.StaffAssigned);

            // Update grid
            for (int h = start; h < start + task.Duration; h++)
            {
                timetableGrid[day][h] = $"{task.SubjectCode} ({task.StaffAssigned})";
            }

            // Update staff occupancy
            if (!staffOcc.ContainsKey(staffCode))
                staffOcc[staffCode] = new Dictionary<string, HashSet<int>>();
            if (!staffOcc[staffCode].ContainsKey(day))
                staffOcc[staffCode][day] = new HashSet<int>();

            for (int h = start; h < start + task.Duration; h++)
            {
                staffOcc[staffCode][day].Add(h);
            }

            // Update lab occupancy - FIXED: Handle afternoon 4-hour labs correctly
            if (task.IsLab && !string.IsNullOrEmpty(task.LabId))
            {
                if (!labOcc.ContainsKey(task.LabId))
                    labOcc[task.LabId] = new Dictionary<string, HashSet<int>>();
                if (!labOcc[task.LabId].ContainsKey(day))
                    labOcc[task.LabId][day] = new HashSet<int>();

                // For 4-hour labs starting at hour 4 (afternoon), only mark lab hours 5,6,7
                // Hour 4 is controlled by class timetable, not lab timetable
                if (task.Duration == 4 && start == 4)
                {
                    // Afternoon 4-hour lab: mark lab occupancy for hours 5,6,7 only
                    for (int h = 5; h <= 7; h++)
                    {
                        labOcc[task.LabId][day].Add(h);
                    }
                }
                else
                {
                    // Normal lab occupancy for all other cases
                    for (int h = start; h < start + task.Duration; h++)
                    {
                        labOcc[task.LabId][day].Add(h);
                    }
                }
            }

            // Update task state
            task.Day = day;
            task.StartHour = start;
            task.IsPlaced = true;
        }

        private void UnassignTask(TaskUnit task,
            Dictionary<string, Dictionary<string, HashSet<int>>> staffOcc,
            Dictionary<string, Dictionary<string, HashSet<int>>> labOcc,
            Dictionary<string, Dictionary<int, string>> timetableGrid)
        {
            if (!task.IsPlaced) return;

            var day = task.Day;
            var start = task.StartHour;
            var (_, staffCode) = SplitStaff(task.StaffAssigned);

            // Clear grid
            for (int h = start; h < start + task.Duration; h++)
            {
                timetableGrid[day][h] = "---";
            }

            // Clear staff occupancy
            if (staffOcc.ContainsKey(staffCode) && staffOcc[staffCode].ContainsKey(day))
            {
                for (int h = start; h < start + task.Duration; h++)
                {
                    staffOcc[staffCode][day].Remove(h);
                }
            }

            // Clear lab occupancy - FIXED: Handle afternoon 4-hour labs correctly
            if (task.IsLab && !string.IsNullOrEmpty(task.LabId) &&
                labOcc.ContainsKey(task.LabId) && labOcc[task.LabId].ContainsKey(day))
            {
                // For 4-hour labs starting at hour 4 (afternoon), only clear lab hours 5,6,7
                // Hour 4 is controlled by class timetable, not lab timetable
                if (task.Duration == 4 && start == 4)
                {
                    // Afternoon 4-hour lab: clear lab occupancy for hours 5,6,7 only
                    for (int h = 5; h <= 7; h++)
                    {
                        labOcc[task.LabId][day].Remove(h);
                    }
                }
                else
                {
                    // Normal lab occupancy clearing for all other cases
                    for (int h = start; h < start + task.Duration; h++)
                    {
                        labOcc[task.LabId][day].Remove(h);
                    }
                }
            }

            // Clear task state
            task.IsPlaced = false;
            task.Day = null;
            task.StartHour = 0;
        }

        #endregion

        #region Constraint Propagation and Conflict Detection

        private void BuildConflictGraph(List<TaskUnit> tasks)
        {
            // Clear existing conflicts
            foreach (var task in tasks)
                task.Conflicts.Clear();

            // Build conflict relationships
            for (int i = 0; i < tasks.Count; i++)
            {
                for (int j = i + 1; j < tasks.Count; j++)
                {
                    var taskA = tasks[i];
                    var taskB = tasks[j];

                    if (HaveConflict(taskA, taskB))
                    {
                        taskA.Conflicts.Add(taskB);
                        taskB.Conflicts.Add(taskA);
                    }
                }
            }
        }

        private bool HaveConflict(TaskUnit taskA, TaskUnit taskB)
        {
            var (_, staffA) = SplitStaff(taskA.StaffAssigned);
            var (_, staffB) = SplitStaff(taskB.StaffAssigned);

            // Staff conflict
            if (staffA == staffB) return true;

            // Lab conflict
            if (taskA.IsLab && taskB.IsLab &&
                !string.IsNullOrEmpty(taskA.LabId) && !string.IsNullOrEmpty(taskB.LabId) &&
                taskA.LabId == taskB.LabId)
                return true;

            // Check domain overlaps
            foreach (var slotA in taskA.DomainSlots)
            {
                foreach (var slotB in taskB.DomainSlots)
                {
                    if (slotA.day == slotB.day)
                    {
                        int startA = slotA.start;
                        int endA = startA + taskA.Duration - 1;
                        int startB = slotB.start;
                        int endB = startB + taskB.Duration - 1;

                        if (endA >= startB && endB >= startA)
                            return true;
                    }
                }
            }

            return false;
        }

        private List<TaskUnit> GetConflictComponent(TaskUnit task)
        {
            var visited = new HashSet<TaskUnit>();
            var stack = new Stack<TaskUnit>();
            var component = new List<TaskUnit>();

            stack.Push(task);

            while (stack.Count > 0)
            {
                var current = stack.Pop();
                if (visited.Contains(current)) continue;

                visited.Add(current);
                component.Add(current);

                foreach (var neighbor in current.Conflicts)
                {
                    if (!visited.Contains(neighbor))
                        stack.Push(neighbor);
                }
            }

            return component;
        }

        private void ResetConflictComponent(List<TaskUnit> component,
            string[] days,
            int totalHours,
            Dictionary<string, Dictionary<string, HashSet<int>>> staffOcc,
            Dictionary<string, Dictionary<string, HashSet<int>>> labOcc,
            Dictionary<string, Dictionary<int, string>> timetableGrid)
        {
            // Reset only the conflicted tasks, preserving feasible domains where possible
            foreach (var task in component)
            {
                if (task.IsPlaced)
                {
                    // Unassign task but preserve domain if it had valid slots
                    var originalDomain = task.DomainSlots.ToList();
                    UnassignTask(task, staffOcc, labOcc, timetableGrid);

                    // Recalculate domain, but keep original slots that are still valid
                    task.DomainSlots.Clear();
                    foreach (var day in days)
                    {
                        for (int start = 1; start <= totalHours - task.Duration + 1; start++)
                        {
                            if (IsFreeAndNoConflict(task, day, start, staffOcc, labOcc, timetableGrid, totalHours))
                            {
                                task.DomainSlots.Add((day, start));
                            }
                        }
                    }

                    // If new domain is empty but original had slots, try to preserve some
                    if (task.DomainSlots.Count == 0 && originalDomain.Count > 0)
                    {
                        // Add back original slots that don't conflict with currently placed tasks
                        foreach (var slot in originalDomain)
                        {
                            bool stillValid = true;
                            for (int h = slot.start; h < slot.start + task.Duration; h++)
                            {
                                if (timetableGrid.ContainsKey(slot.day) &&
                                    timetableGrid[slot.day].ContainsKey(h) &&
                                    timetableGrid[slot.day][h] != "---")
                                {
                                    stillValid = false;
                                    break;
                                }
                            }
                            if (stillValid)
                                task.DomainSlots.Add(slot);
                        }
                    }
                }
            }
        }

        private bool PropagateConstraints(List<TaskUnit> tasks, TaskUnit assignedTask, (string day, int start) assignedSlot)
        {
            // Create snapshot of domains before propagation
            var domainSnapshot = tasks.ToDictionary(t => t, t => t.DomainSlots.ToList());

            // Set assigned task domain to single slot
            assignedTask.DomainSlots = new List<(string, int)> { assignedSlot };

            var queue = new Queue<TaskUnit>();
            var processedTasks = new HashSet<TaskUnit>();

            // Add all unplaced conflicted tasks to queue
            foreach (var task in tasks)
            {
                if (task != assignedTask && !task.IsPlaced && task.Conflicts.Contains(assignedTask))
                    queue.Enqueue(task);
            }

            while (queue.Count > 0)
            {
                var task = queue.Dequeue();
                if (task.IsPlaced || task == assignedTask || processedTasks.Contains(task))
                    continue;

                processedTasks.Add(task);
                var originalDomainSize = task.DomainSlots.Count;
                var filteredDomain = new List<(string day, int start)>();

                // Only prune conflicting slots, not entire domain
                foreach (var slot in task.DomainSlots)
                {
                    bool hasConflict = false;

                    // Check conflict with assigned task only if on same day
                    if (slot.day == assignedSlot.day)
                    {
                        int start1 = slot.start;
                        int end1 = slot.start + task.Duration - 1;
                        int start2 = assignedSlot.start;
                        int end2 = assignedSlot.start + assignedTask.Duration - 1;

                        // Check time overlap
                        bool timeOverlap = end1 >= start2 && end2 >= start1;

                        if (timeOverlap)
                        {
                            var (_, staff1) = SplitStaff(task.StaffAssigned);
                            var (_, staff2) = SplitStaff(assignedTask.StaffAssigned);

                            // Staff conflict
                            if (staff1 == staff2)
                                hasConflict = true;
                            // Lab conflict
                            else if (task.IsLab && assignedTask.IsLab &&
                                     !string.IsNullOrEmpty(task.LabId) &&
                                     !string.IsNullOrEmpty(assignedTask.LabId) &&
                                     task.LabId == assignedTask.LabId)
                                hasConflict = true;
                        }
                    }

                    if (!hasConflict)
                        filteredDomain.Add(slot);
                }

                // Update domain only if slots were actually pruned
                if (filteredDomain.Count < originalDomainSize)
                {
                    task.DomainSlots = filteredDomain;

                    // If domain becomes empty, restore all domains and fail
                    if (task.DomainSlots.Count == 0)
                    {
                        foreach (var kvp in domainSnapshot)
                            kvp.Key.DomainSlots = kvp.Value;
                        return false;
                    }

                    // Add neighbors of affected task to queue for further propagation
                    foreach (var neighbor in task.Conflicts)
                    {
                        if (!neighbor.IsPlaced && neighbor != assignedTask && !queue.Contains(neighbor) && !processedTasks.Contains(neighbor))
                        {
                            queue.Enqueue(neighbor);
                        }
                    }
                }
            }

            return true;
        }

        private bool TryReschedulingToMakeRoom(TaskUnit newTask, List<TaskUnit> allTasks,
            string[] days, int totalHours,
            Dictionary<string, Dictionary<string, HashSet<int>>> staffOcc,
            Dictionary<string, Dictionary<string, HashSet<int>>> labOcc,
            Dictionary<string, Dictionary<int, string>> timetableGrid)
        {
            Console.WriteLine($"Attempting rescheduling for {newTask.SubjectCode} (Duration: {newTask.Duration}, IsLab: {newTask.IsLab})");

            // Find potential slots where this task could fit if we move other tasks
            var potentialSlots = new List<(string day, int start, List<TaskUnit> conflictingTasks)>();

            foreach (var day in days)
            {
                for (int start = 1; start <= totalHours - newTask.Duration + 1; start++)
                {
                    // Check lab-specific constraints first
                    if (newTask.IsLab)
                    {
                        if (newTask.Duration == 4 && !(start == 1 || start == 4))
                            continue;
                        if (newTask.Duration == 3 && !(start == 1 || start == 4 || start == 5))
                            continue;
                        if (newTask.Duration == 2 && (start < 1 || start > 6))
                            continue;
                    }

                    // Find what tasks are blocking this slot
                    var conflictingTasks = new List<TaskUnit>();
                    var (_, newTaskStaffCode) = SplitStaff(newTask.StaffAssigned);

                    for (int h = start; h < start + newTask.Duration; h++)
                    {
                        // Check grid conflicts
                        if (timetableGrid.ContainsKey(day) && timetableGrid[day].ContainsKey(h) && timetableGrid[day][h] != "---")
                        {
                            // Find the task occupying this slot
                            var occupyingTask = allTasks.FirstOrDefault(t =>
                                t.IsPlaced && t.Day == day &&
                                t.StartHour <= h && h < t.StartHour + t.Duration);

                            if (occupyingTask != null && !conflictingTasks.Contains(occupyingTask))
                            {
                                conflictingTasks.Add(occupyingTask);
                            }
                        }

                        // Check staff conflicts
                        if (staffOcc.TryGetValue(newTaskStaffCode, out var staffDayMap) &&
                            staffDayMap.TryGetValue(day, out var staffHours) &&
                            staffHours.Contains(h))
                        {
                            // Find staff conflicting task
                            var staffConflictTask = allTasks.FirstOrDefault(t =>
                                t.IsPlaced && t.Day == day &&
                                t.StartHour <= h && h < t.StartHour + t.Duration &&
                                SplitStaff(t.StaffAssigned).Item2 == newTaskStaffCode);

                            if (staffConflictTask != null && !conflictingTasks.Contains(staffConflictTask))
                            {
                                conflictingTasks.Add(staffConflictTask);
                            }
                        }

                        // Check lab conflicts
                        if (newTask.IsLab && !string.IsNullOrEmpty(newTask.LabId) &&
                            labOcc.TryGetValue(newTask.LabId, out var labDayMap) &&
                            labDayMap.TryGetValue(day, out var labHours) &&
                            labHours.Contains(h))
                        {
                            // Find lab conflicting task
                            var labConflictTask = allTasks.FirstOrDefault(t =>
                                t.IsPlaced && t.IsLab && t.Day == day &&
                                t.StartHour <= h && h < t.StartHour + t.Duration &&
                                t.LabId == newTask.LabId);

                            if (labConflictTask != null && !conflictingTasks.Contains(labConflictTask))
                            {
                                conflictingTasks.Add(labConflictTask);
                            }
                        }
                    }

                    if (conflictingTasks.Count > 0 && conflictingTasks.Count <= 3) // Limit rescheduling complexity
                    {
                        potentialSlots.Add((day, start, conflictingTasks));
                    }
                }
            }

            // Sort potential slots by number of conflicts (prefer fewer conflicts)
            potentialSlots = potentialSlots.OrderBy(slot => slot.conflictingTasks.Count).ToList();

            Console.WriteLine($"Found {potentialSlots.Count} potential rescheduling opportunities");

            // Try each potential slot, prioritizing easier rescheduling scenarios
            foreach (var (day, start, conflictingTasks) in potentialSlots)
            {
                Console.WriteLine($"Trying to reschedule {conflictingTasks.Count} tasks to make room at {day} hour {start}");

                // Skip if trying to reschedule labs with new theory or vice versa (harder constraints)
                if (newTask.IsLab && conflictingTasks.Any(t => t.IsLab))
                {
                    bool shouldSkip = false;
                    foreach (var conflictTask in conflictingTasks.Where(t => t.IsLab))
                    {
                        // Don't try to reschedule 4-hour labs easily - they have strict constraints
                        if (conflictTask.Duration == 4)
                        {
                            shouldSkip = true;
                            break;
                        }
                    }
                    if (shouldSkip)
                    {
                        Console.WriteLine($"  Skipping - involves rescheduling constrained lab tasks");
                        continue;
                    }
                }

                // Store original positions
                var originalPositions = conflictingTasks.ToDictionary(t => t, t => (t.Day, t.StartHour));

                // Temporarily unassign conflicting tasks
                foreach (var conflictTask in conflictingTasks)
                {
                    UnassignTask(conflictTask, staffOcc, labOcc, timetableGrid);
                }

                // Try to find new positions for all conflicting tasks
                bool canRescheduleAll = true;
                var newPositions = new Dictionary<TaskUnit, (string day, int start)>();

                foreach (var conflictTask in conflictingTasks)
                {
                    var foundNewSlot = false;

                    // Try to find a new slot for this task
                    foreach (var tryDay in days)
                    {
                        for (int tryStart = 1; tryStart <= totalHours - conflictTask.Duration + 1; tryStart++)
                        {
                            if (IsFreeAndNoConflict(conflictTask, tryDay, tryStart, staffOcc, labOcc, timetableGrid, totalHours))
                            {
                                newPositions[conflictTask] = (tryDay, tryStart);
                                AssignTask(conflictTask, (tryDay, tryStart), staffOcc, labOcc, timetableGrid);
                                foundNewSlot = true;
                                Console.WriteLine($"  Rescheduled {conflictTask.SubjectCode} from {originalPositions[conflictTask].Day} {originalPositions[conflictTask].StartHour} to {tryDay} {tryStart}");
                                break;
                            }
                        }
                        if (foundNewSlot) break;
                    }

                    if (!foundNewSlot)
                    {
                        canRescheduleAll = false;
                        Console.WriteLine($"  Cannot reschedule {conflictTask.SubjectCode}");
                        break;
                    }
                }

                if (canRescheduleAll)
                {
                    Console.WriteLine($"✅ Successfully rescheduled all conflicting tasks for slot {day} {start}");
                    return true;
                }
                else
                {
                    // Restore original positions
                    Console.WriteLine($"❌ Failed to reschedule all tasks, restoring original positions");
                    foreach (var conflictTask in conflictingTasks)
                    {
                        UnassignTask(conflictTask, staffOcc, labOcc, timetableGrid);
                    }

                    foreach (var conflictTask in conflictingTasks)
                    {
                        var (originalDay, originalStart) = originalPositions[conflictTask];
                        AssignTask(conflictTask, (originalDay, originalStart), staffOcc, labOcc, timetableGrid);
                    }
                }
            }

            // Try advanced rescheduling with chain moves (move A to B, move B to C, etc.)
            Console.WriteLine($"Attempting advanced chain rescheduling for {newTask.SubjectCode}");
            if (TryChainRescheduling(newTask, allTasks, days, totalHours, staffOcc, labOcc, timetableGrid))
            {
                return true;
            }

            Console.WriteLine($"❌ No successful rescheduling found for {newTask.SubjectCode}");
            return false;
        }

        private bool TryChainRescheduling(TaskUnit newTask, List<TaskUnit> allTasks,
            string[] days, int totalHours,
            Dictionary<string, Dictionary<string, HashSet<int>>> staffOcc,
            Dictionary<string, Dictionary<string, HashSet<int>>> labOcc,
            Dictionary<string, Dictionary<int, string>> timetableGrid)
        {
            // Try chain rescheduling: move task A to make room, which creates room for task B, etc.
            var placedTasks = allTasks.Where(t => t.IsPlaced).ToList();

            foreach (var taskToMove in placedTasks.Take(Math.Min(5, placedTasks.Count))) // Limit to avoid infinite loops
            {
                // Store original position
                var originalDay = taskToMove.Day;
                var originalStart = taskToMove.StartHour;

                // Temporarily unassign this task
                UnassignTask(taskToMove, staffOcc, labOcc, timetableGrid);

                // Check if new task can fit in the freed space
                var canFitNewTask = false;
                for (int checkStart = Math.Max(1, originalStart - newTask.Duration + 1);
                     checkStart <= Math.Min(totalHours - newTask.Duration + 1, originalStart + taskToMove.Duration - 1);
                     checkStart++)
                {
                    if (IsFreeAndNoConflict(newTask, originalDay, checkStart, staffOcc, labOcc, timetableGrid, totalHours))
                    {
                        canFitNewTask = true;
                        break;
                    }
                }

                if (canFitNewTask)
                {
                    // Try to find new position for moved task
                    foreach (var tryDay in days)
                    {
                        for (int tryStart = 1; tryStart <= totalHours - taskToMove.Duration + 1; tryStart++)
                        {
                            if (IsFreeAndNoConflict(taskToMove, tryDay, tryStart, staffOcc, labOcc, timetableGrid, totalHours))
                            {
                                // Assign moved task to new position
                                AssignTask(taskToMove, (tryDay, tryStart), staffOcc, labOcc, timetableGrid);

                                // Verify new task can still fit
                                for (int checkStart = Math.Max(1, originalStart - newTask.Duration + 1);
                                     checkStart <= Math.Min(totalHours - newTask.Duration + 1, originalStart + taskToMove.Duration - 1);
                                     checkStart++)
                                {
                                    if (IsFreeAndNoConflict(newTask, originalDay, checkStart, staffOcc, labOcc, timetableGrid, totalHours))
                                    {
                                        Console.WriteLine($"✅ Chain rescheduling successful: moved {taskToMove.SubjectCode} from {originalDay} {originalStart} to {tryDay} {tryStart}");
                                        return true;
                                    }
                                }

                                // If new task doesn't fit, restore and try next position
                                UnassignTask(taskToMove, staffOcc, labOcc, timetableGrid);
                                AssignTask(taskToMove, (originalDay, originalStart), staffOcc, labOcc, timetableGrid);
                                return false; // Exit this attempt
                            }
                        }
                    }
                }

                // Restore original position
                AssignTask(taskToMove, (originalDay, originalStart), staffOcc, labOcc, timetableGrid);
            }

            return false;
        }

        #endregion

        #region Enhanced Domain Assignment with GA Fallback

        private bool AssignDomains(List<TaskUnit> tasks,
            string[] days,
            int totalHours,
            Dictionary<string, Dictionary<string, HashSet<int>>> staffOcc,
            Dictionary<string, Dictionary<string, HashSet<int>>> labOcc,
            Dictionary<string, Dictionary<int, string>> timetableGrid)
        {
            // Simple domain assignment like original code for better accuracy
            foreach (var task in tasks)
            {
                task.DomainSlots.Clear();

                foreach (var day in days)
                {
                    for (int start = 1; start <= totalHours - task.Duration + 1; start++)
                    {
                        if (IsFreeAndNoConflict(task, day, start, staffOcc, labOcc, timetableGrid, totalHours))
                        {
                            task.DomainSlots.Add((day, start));
                        }
                    }
                }

                // Enhanced rescheduling approach instead of immediate failure
                if (task.DomainSlots.Count == 0)
                {
                    Console.WriteLine($"No initial slot for task {task.SubjectCode}. Attempting rescheduling...");

                    // Try rescheduling existing tasks to make room
                    if (TryReschedulingToMakeRoom(task, tasks, days, totalHours, staffOcc, labOcc, timetableGrid))
                    {
                        Console.WriteLine($"✅ Rescheduling successful for {task.SubjectCode}");
                        // Recalculate domain after rescheduling
                        task.DomainSlots.Clear();
                        foreach (var day in days)
                        {
                            for (int start = 1; start <= totalHours - task.Duration + 1; start++)
                            {
                                if (IsFreeAndNoConflict(task, day, start, staffOcc, labOcc, timetableGrid, totalHours))
                                {
                                    task.DomainSlots.Add((day, start));
                                }
                            }
                        }

                        if (task.DomainSlots.Count == 0)
                        {
                            Console.WriteLine($"❌ Rescheduling failed for {task.SubjectCode} - still no valid slots");
                            return false;
                        }
                    }
                    else
                    {
                        Console.WriteLine($"❌ Rescheduling failed for {task.SubjectCode}");
                        return false;
                    }
                }

                // Shuffle domain slots like original code
                Shuffle(task.DomainSlots);
            }

            return true;
        }

        #endregion

        #region Genetic Algorithm Fallback Methods

        private (bool success, (string day, int start) slot) TryGAFallbackSingleTask(
            TaskUnit task,
            string[] days,
            int totalHours,
            Dictionary<string, Dictionary<string, HashSet<int>>> staffOcc,
            Dictionary<string, Dictionary<string, HashSet<int>>> labOcc,
            Dictionary<string, Dictionary<int, string>> timetableGrid)
        {
            // Create potential slots by relaxing some constraints
            var potentialSlots = new List<(string day, int start)>();

            foreach (var day in days)
            {
                for (int start = 1; start <= totalHours - task.Duration + 1; start++)
                {
                    // Relax constraints - only check basic time and grid availability
                    bool canPlace = true;

                    // Check time bounds
                    if (start < 1 || start + task.Duration - 1 > totalHours)
                        canPlace = false;

                    // Enhanced lab-specific constraints
                    if (task.IsLab)
                    {
                        if (task.Duration == 4)
                        {
                            // 4-hour labs: can start at hour 1 or 4
                            if (!(start == 1 || start == 4))
                                canPlace = false;
                        }
                        else if (task.Duration == 3)
                        {
                            // 3-hour labs: can start at hour 1, 4, or 5
                            if (!(start == 1 || start == 4 || start == 5))
                                canPlace = false;
                        }
                        else if (task.Duration == 2)
                        {
                            // 2-hour embedded labs: can start at hours 1-6
                            if (start < 1 || start > 6)
                                canPlace = false;
                        }
                    }

                    if (canPlace)
                        potentialSlots.Add((day, start));
                }
            }

            if (potentialSlots.Count == 0)
                return (false, ("", 0));

            // Simple GA for single task
            var population = new List<GAIndividual>();

            // Create initial population
            for (int i = 0; i < Math.Min(20, potentialSlots.Count); i++)
            {
                var individual = new GAIndividual();
                individual.Assignments[task] = potentialSlots[rng.Next(potentialSlots.Count)];
                individual.Fitness = EvaluateSingleTaskFitness(task, individual.Assignments[task], staffOcc, labOcc, timetableGrid);
                population.Add(individual);
            }

            // Evolution
            for (int gen = 0; gen < 50; gen++)
            {
                population = population.OrderBy(ind => ind.Fitness).ToList();

                if (population[0].Fitness == 0)
                {
                    return (true, population[0].Assignments[task]);
                }

                // Create next generation
                var nextGen = new List<GAIndividual> { population[0] }; // Elitism

                while (nextGen.Count < population.Count)
                {
                    var parent = population[rng.Next(Math.Min(5, population.Count))]; // Tournament selection
                    var child = parent.Clone();

                    // Mutation
                    if (rng.NextDouble() < 0.3)
                    {
                        child.Assignments[task] = potentialSlots[rng.Next(potentialSlots.Count)];
                    }

                    child.Fitness = EvaluateSingleTaskFitness(task, child.Assignments[task], staffOcc, labOcc, timetableGrid);
                    nextGen.Add(child);
                }

                population = nextGen;
            }

            population = population.OrderBy(ind => ind.Fitness).ToList();

            // Accept solution if fitness is reasonable (allow some conflicts)
            if (population[0].Fitness < 5)
            {
                return (true, population[0].Assignments[task]);
            }

            return (false, ("", 0));
        }

        private (bool success, Dictionary<TaskUnit, (string day, int start)> assignments) TryGAFallbackComponent(
            List<TaskUnit> component,
            string[] days,
            int totalHours,
            Dictionary<string, Dictionary<string, HashSet<int>>> staffOcc,
            Dictionary<string, Dictionary<string, HashSet<int>>> labOcc,
            Dictionary<string, Dictionary<int, string>> timetableGrid)
        {
            if (component.Count > MAX_GENETIC_COMPONENT_SIZE)
                return (false, null);

            var population = new List<GAIndividual>();

            // Create initial population
            for (int i = 0; i < GA_POPULATION_SIZE; i++)
            {
                var individual = CreateRandomIndividual(component, days, totalHours);
                if (individual != null)
                {
                    individual.Fitness = EvaluateComponentFitness(component, individual, staffOcc, labOcc, timetableGrid);
                    population.Add(individual);
                }
            }

            if (population.Count == 0)
                return (false, null);

            // Evolution
            for (int gen = 0; gen < GA_MAX_GENERATIONS; gen++)
            {
                population = population.OrderBy(ind => ind.Fitness).ToList();

                if (population[0].Fitness == 0)
                {
                    return (true, population[0].Assignments);
                }

                // Create next generation
                var nextGen = new List<GAIndividual> { population[0], population[1] }; // Elitism

                while (nextGen.Count < population.Count)
                {
                    var parent1 = TournamentSelection(population);
                    var parent2 = TournamentSelection(population);
                    var (child1, child2) = Crossover(parent1, parent2, component);

                    Mutate(child1, component, days, totalHours);
                    Mutate(child2, component, days, totalHours);

                    child1.Fitness = EvaluateComponentFitness(component, child1, staffOcc, labOcc, timetableGrid);
                    child2.Fitness = EvaluateComponentFitness(component, child2, staffOcc, labOcc, timetableGrid);

                    nextGen.Add(child1);
                    if (nextGen.Count < population.Count)
                        nextGen.Add(child2);
                }

                population = nextGen;
            }

            population = population.OrderBy(ind => ind.Fitness).ToList();

            // Accept solution if fitness is reasonable
            if (population[0].Fitness < component.Count * 3)
            {
                return (true, population[0].Assignments);
            }

            return (false, null);
        }

        private GAIndividual CreateRandomIndividual(List<TaskUnit> tasks, string[] days, int totalHours)
        {
            var individual = new GAIndividual();

            foreach (var task in tasks)
            {
                var validSlots = new List<(string day, int start)>();

                foreach (var day in days)
                {
                    for (int start = 1; start <= totalHours - task.Duration + 1; start++)
                    {
                        bool canPlace = true;

                        // Basic time bounds check
                        if (start < 1 || start + task.Duration - 1 > totalHours)
                            canPlace = false;

                        // Enhanced lab-specific constraints
                        if (task.IsLab)
                        {
                            if (task.Duration == 4)
                            {
                                // 4-hour labs: can start at hour 1 or 4
                                if (!(start == 1 || start == 4))
                                    canPlace = false;
                            }
                            else if (task.Duration == 3)
                            {
                                // 3-hour labs: can start at hour 1, 4, or 5
                                if (!(start == 1 || start == 4 || start == 5))
                                    canPlace = false;
                            }
                            else if (task.Duration == 2)
                            {
                                // 2-hour embedded labs: can start at hours 1-6
                                if (start < 1 || start > 6)
                                    canPlace = false;
                            }
                        }

                        if (canPlace)
                            validSlots.Add((day, start));
                    }
                }

                if (validSlots.Count == 0)
                    return null;

                individual.Assignments[task] = validSlots[rng.Next(validSlots.Count)];
            }

            return individual;
        }

        private int EvaluateSingleTaskFitness(TaskUnit task, (string day, int start) slot,
            Dictionary<string, Dictionary<string, HashSet<int>>> staffOcc,
            Dictionary<string, Dictionary<string, HashSet<int>>> labOcc,
            Dictionary<string, Dictionary<int, string>> timetableGrid)
        {
            int penalty = 0;
            var (_, staffCode) = SplitStaff(task.StaffAssigned);

            for (int h = slot.start; h < slot.start + task.Duration; h++)
            {
                // Grid conflict
                if (timetableGrid.ContainsKey(slot.day) &&
                    timetableGrid[slot.day].ContainsKey(h) &&
                    timetableGrid[slot.day][h] != "---")
                    penalty += 10;

                // Staff conflict
                if (staffOcc.ContainsKey(staffCode) &&
                    staffOcc[staffCode].ContainsKey(slot.day) &&
                    staffOcc[staffCode][slot.day].Contains(h))
                    penalty += 5;

                // Lab conflict - FIXED: Handle afternoon 4-hour labs correctly
                if (task.IsLab && !string.IsNullOrEmpty(task.LabId) &&
                    labOcc.ContainsKey(task.LabId) &&
                    labOcc[task.LabId].ContainsKey(slot.day))
                {
                    // For 4-hour labs starting at hour 4 (afternoon), only check lab hours 5,6,7
                    if (task.Duration == 4 && slot.start == 4)
                    {
                        if (h >= 5 && h <= 7 && labOcc[task.LabId][slot.day].Contains(h))
                            penalty += 5;
                    }
                    else
                    {
                        // Normal lab conflict check for all other cases
                        if (labOcc[task.LabId][slot.day].Contains(h))
                            penalty += 5;
                    }
                }
            }

            // Enhanced lab time constraint penalty
            if (task.IsLab)
            {
                if (task.Duration == 4)
                {
                    // 4-hour labs: penalty if not starting at hour 1 or 4
                    if (!(slot.start == 1 || slot.start == 4))
                        penalty += 20;
                }
                else if (task.Duration == 3)
                {
                    // 3-hour labs: penalty if not starting at hour 1, 4, or 5
                    if (!(slot.start == 1 || slot.start == 4 || slot.start == 5))
                        penalty += 15;
                }
                else if (task.Duration == 2)
                {
                    // 2-hour embedded labs: penalty if starting outside hours 1-6
                    if (slot.start < 1 || slot.start > 6)
                        penalty += 10;
                }
            }

            return penalty;
        }

        private int EvaluateComponentFitness(List<TaskUnit> component, GAIndividual individual,
            Dictionary<string, Dictionary<string, HashSet<int>>> staffOcc,
            Dictionary<string, Dictionary<string, HashSet<int>>> labOcc,
            Dictionary<string, Dictionary<int, string>> timetableGrid)
        {
            int penalty = 0;
            var internalStaffSchedule = new Dictionary<string, Dictionary<string, HashSet<int>>>();
            var internalLabSchedule = new Dictionary<string, Dictionary<string, HashSet<int>>>();
            var dayDistribution = new Dictionary<string, int>(); // Track task distribution across days

            foreach (var task in component)
            {
                if (!individual.Assignments.TryGetValue(task, out var slot))
                    continue;

                var (_, staffCode) = SplitStaff(task.StaffAssigned);

                // Count tasks per day for balanced distribution
                if (!dayDistribution.ContainsKey(slot.day))
                    dayDistribution[slot.day] = 0;
                dayDistribution[slot.day]++;

                for (int h = slot.start; h < slot.start + task.Duration; h++)
                {
                    // External conflicts
                    if (timetableGrid.ContainsKey(slot.day) &&
                        timetableGrid[slot.day].ContainsKey(h) &&
                        timetableGrid[slot.day][h] != "---")
                        penalty += 10;

                    if (staffOcc.ContainsKey(staffCode) &&
                        staffOcc[staffCode].ContainsKey(slot.day) &&
                        staffOcc[staffCode][slot.day].Contains(h))
                        penalty += 8;

                    // Lab conflict - FIXED: Handle afternoon 4-hour labs correctly  
                    if (task.IsLab && !string.IsNullOrEmpty(task.LabId) &&
                        labOcc.ContainsKey(task.LabId) &&
                        labOcc[task.LabId].ContainsKey(slot.day))
                    {
                        // For 4-hour labs starting at hour 4 (afternoon), only check lab hours 5,6,7
                        if (task.Duration == 4 && slot.start == 4)
                        {
                            if (h >= 5 && h <= 7 && labOcc[task.LabId][slot.day].Contains(h))
                                penalty += 8;
                        }
                        else
                        {
                            // Normal lab conflict check for all other cases
                            if (labOcc[task.LabId][slot.day].Contains(h))
                                penalty += 8;
                        }
                    }

                    // Internal conflicts
                    if (!internalStaffSchedule.ContainsKey(staffCode))
                        internalStaffSchedule[staffCode] = new Dictionary<string, HashSet<int>>();
                    if (!internalStaffSchedule[staffCode].ContainsKey(slot.day))
                        internalStaffSchedule[staffCode][slot.day] = new HashSet<int>();

                    if (internalStaffSchedule[staffCode][slot.day].Contains(h))
                        penalty += 15;
                    else
                        internalStaffSchedule[staffCode][slot.day].Add(h);

                    if (task.IsLab && !string.IsNullOrEmpty(task.LabId))
                    {
                        if (!internalLabSchedule.ContainsKey(task.LabId))
                            internalLabSchedule[task.LabId] = new Dictionary<string, HashSet<int>>();
                        if (!internalLabSchedule[task.LabId].ContainsKey(slot.day))
                            internalLabSchedule[task.LabId][slot.day] = new HashSet<int>();

                        if (internalLabSchedule[task.LabId][slot.day].Contains(h))
                            penalty += 15;
                        else
                            internalLabSchedule[task.LabId][slot.day].Add(h);
                    }
                }

                // Enhanced lab time constraint penalty
                if (task.IsLab)
                {
                    if (task.Duration == 4)
                    {
                        // 4-hour labs: penalty if not starting at hour 1 or 4
                        if (!(slot.start == 1 || slot.start == 4))
                            penalty += 20;
                    }
                    else if (task.Duration == 3)
                    {
                        // 3-hour labs: penalty if not starting at hour 1, 4, or 5
                        if (!(slot.start == 1 || slot.start == 4 || slot.start == 5))
                            penalty += 15;
                    }
                    else if (task.Duration == 2)
                    {
                        // 2-hour embedded labs: penalty if starting outside hours 1-6
                        if (slot.start < 1 || slot.start > 6)
                            penalty += 10;
                    }
                }
            }

            // Reward balanced distribution across days
            if (dayDistribution.Count > 1)
            {
                var maxTasksPerDay = dayDistribution.Values.Max();
                var minTasksPerDay = dayDistribution.Values.Min();
                var imbalance = maxTasksPerDay - minTasksPerDay;
                penalty += imbalance * 2; // Small penalty for imbalanced distribution
            }

            return penalty;
        }

        private GAIndividual TournamentSelection(List<GAIndividual> population)
        {
            int k = 3; // Tournament size
            var selected = new List<GAIndividual>();

            for (int i = 0; i < k; i++)
            {
                selected.Add(population[rng.Next(population.Count)]);
            }

            return selected.OrderBy(ind => ind.Fitness).First();
        }

        private (GAIndividual child1, GAIndividual child2) Crossover(GAIndividual parent1, GAIndividual parent2, List<TaskUnit> tasks)
        {
            var child1 = new GAIndividual();
            var child2 = new GAIndividual();

            int crossoverPoint = rng.Next(1, tasks.Count);

            for (int i = 0; i < tasks.Count; i++)
            {
                var task = tasks[i];

                if (i < crossoverPoint)
                {
                    if (parent1.Assignments.ContainsKey(task))
                        child1.Assignments[task] = parent1.Assignments[task];
                    if (parent2.Assignments.ContainsKey(task))
                        child2.Assignments[task] = parent2.Assignments[task];
                }
                else
                {
                    if (parent2.Assignments.ContainsKey(task))
                        child1.Assignments[task] = parent2.Assignments[task];
                    if (parent1.Assignments.ContainsKey(task))
                        child2.Assignments[task] = parent1.Assignments[task];
                }
            }

            return (child1, child2);
        }

        private void Mutate(GAIndividual individual, List<TaskUnit> tasks, string[] days, int totalHours)
        {
            // Adaptive mutation rate based on component size
            double adaptiveMutationRate = tasks.Count <= 5 ? 0.05 : GA_MUTATION_RATE;

            foreach (var task in tasks)
            {
                if (rng.NextDouble() < adaptiveMutationRate)
                {
                    var validSlots = new List<(string day, int start)>();

                    foreach (var day in days)
                    {
                        for (int start = 1; start <= totalHours - task.Duration + 1; start++)
                        {
                            bool canPlace = true;

                            // Ensure 1-based hour indexing and duration constraints
                            if (start < 1 || start + task.Duration - 1 > totalHours)
                                canPlace = false;

                            // Lab-specific constraints: 4-hour labs must start at 1 or 4
                            if (task.IsLab && task.Duration == 4 && !(start == 1 || start == 4))
                                canPlace = false;

                            if (canPlace)
                                validSlots.Add((day, start));
                        }
                    }

                    if (validSlots.Count > 0)
                    {
                        individual.Assignments[task] = validSlots[rng.Next(validSlots.Count)];
                    }
                }
            }
        }

        private async Task<(bool Succeeded, List<TaskUnit> Result)> RunGeneticAlgorithmAsync(
            List<TaskUnit> tasksToAssign,
            string[] days,
            int hours,
            Dictionary<string, Dictionary<string, HashSet<int>>> staffOcc,
            Dictionary<string, Dictionary<string, HashSet<int>>> labOcc)
        {
            const int populationSize = 50;
            const int maxGenerations = 150;
            const double mutationRate = 0.15;

            bool CanPlace(TaskUnit t, string day, int start)
            {
                if (start < 1 || start + t.Duration - 1 > hours)
                    return false;

                var (_, staffCode) = SplitStaff(t.StaffAssigned);
                for (int h = start; h < start + t.Duration; h++)
                {
                    if (staffOcc.TryGetValue(staffCode, out var dayMap) && dayMap.TryGetValue(day, out var staffHours) && staffHours.Contains(h))
                        return false;
                    if (t.IsLab && !string.IsNullOrEmpty(t.LabId))
                    {
                        // FIXED: Handle afternoon 4-hour labs correctly
                        if (t.Duration == 4 && start == 4)
                        {
                            // For afternoon 4-hour labs, only check lab hours 5,6,7
                            if (h >= 5 && h <= 7 && labOcc.TryGetValue(t.LabId, out var labDayMap) && labDayMap.TryGetValue(day, out var labHours) && labHours.Contains(h))
                                return false;
                        }
                        else
                        {
                            // Normal lab conflict check for all other cases
                            if (labOcc.TryGetValue(t.LabId, out var labDayMap) && labDayMap.TryGetValue(day, out var labHours) && labHours.Contains(h))
                                return false;
                        }
                    }
                }
                if (t.IsLab && t.Duration == 4 && !(start == 1 || start == 4))
                    return false;

                return true;
            }

            List<TaskUnit> CreateRandomIndividual()
            {
                var individual = new List<TaskUnit>();
                foreach (var t in tasksToAssign)
                {
                    var validSlots = new List<(string day, int start)>();
                    foreach (var day in days)
                    {
                        for (int start = 1; start <= hours - t.Duration + 1; start++)
                        {
                            if (CanPlace(t, day, start))
                                validSlots.Add((day, start));
                        }
                    }
                    if (validSlots.Count == 0)
                        return null;
                    var chosen = validSlots[rng.Next(validSlots.Count)];
                    var copy = t.Clone();
                    copy.Day = chosen.day;
                    copy.StartHour = chosen.start;
                    copy.IsPlaced = true;
                    individual.Add(copy);
                }
                return individual;
            }

            int Fitness(List<TaskUnit> individual)
            {
                int penalty = 0;
                var staffSchedule = new Dictionary<string, Dictionary<string, HashSet<int>>>();
                var labSchedule = new Dictionary<string, Dictionary<string, HashSet<int>>>();

                foreach (var t in tasksToAssign)
                {
                    var (_, staffCode) = SplitStaff(t.StaffAssigned);
                    if (!staffSchedule.ContainsKey(staffCode))
                        staffSchedule[staffCode] = days.ToDictionary(d => d, _ => new HashSet<int>());
                    if (t.IsLab && !string.IsNullOrEmpty(t.LabId) && !labSchedule.ContainsKey(t.LabId))
                        labSchedule[t.LabId] = days.ToDictionary(d => d, _ => new HashSet<int>());
                }

                foreach (var t in individual)
                {
                    var (_, staffCode) = SplitStaff(t.StaffAssigned);
                    for (int h = t.StartHour; h < t.StartHour + t.Duration; h++)
                    {
                        if (staffOcc.TryGetValue(staffCode, out var dayMap) && dayMap.TryGetValue(t.Day, out var existingHours) && existingHours.Contains(h))
                            penalty += 10;

                        // FIXED: Handle afternoon 4-hour labs correctly
                        if (t.IsLab && !string.IsNullOrEmpty(t.LabId) && labOcc.TryGetValue(t.LabId, out var labDayMap) && labDayMap.TryGetValue(t.Day, out var labExisting))
                        {
                            // For 4-hour labs starting at hour 4 (afternoon), only check lab hours 5,6,7
                            if (t.Duration == 4 && t.StartHour == 4)
                            {
                                if (h >= 5 && h <= 7 && labExisting.Contains(h))
                                    penalty += 10;
                            }
                            else
                            {
                                // Normal lab conflict check for all other cases
                                if (labExisting.Contains(h))
                                    penalty += 10;
                            }
                        }

                        if (staffSchedule[staffCode][t.Day].Contains(h))
                            penalty += 5;
                        else
                            staffSchedule[staffCode][t.Day].Add(h);

                        if (t.IsLab && !string.IsNullOrEmpty(t.LabId))
                        {
                            if (labSchedule[t.LabId][t.Day].Contains(h))
                                penalty += 5;
                            else
                                labSchedule[t.LabId][t.Day].Add(h);
                        }
                    }

                    if (t.IsLab && t.Duration == 4 && !(t.StartHour == 1 || t.StartHour == 4))
                        penalty += 20;
                }
                return penalty;
            }

            List<TaskUnit> TournamentSelection(List<List<TaskUnit>> population)
            {
                int k = 3;
                var selected = new List<List<TaskUnit>>();
                for (int i = 0; i < k; i++)
                    selected.Add(population[rng.Next(population.Count)]);
                return selected.OrderBy(ind => Fitness(ind)).First();
            }

            (List<TaskUnit>, List<TaskUnit>) Crossover(List<TaskUnit> parent1, List<TaskUnit> parent2)
            {
                int point = rng.Next(1, parent1.Count);
                var child1 = new List<TaskUnit>();
                var child2 = new List<TaskUnit>();
                for (int i = 0; i < parent1.Count; i++)
                {
                    child1.Add(i < point ? parent1[i] : parent2[i]);
                    child2.Add(i < point ? parent2[i] : parent1[i]);
                }
                return (child1, child2);
            }

            void Mutate(List<TaskUnit> individual)
            {
                for (int i = 0; i < individual.Count; i++)
                {
                    if (rng.NextDouble() < mutationRate)
                    {
                        var t = tasksToAssign[i];
                        var validSlots = new List<(string day, int start)>();
                        foreach (var day in days)
                        {
                            for (int start = 1; start <= hours - t.Duration + 1; start++)
                            {
                                if (CanPlace(t, day, start))
                                    validSlots.Add((day, start));
                            }
                        }
                        if (validSlots.Count > 0)
                        {
                            var chosen = validSlots[rng.Next(validSlots.Count)];
                            individual[i].Day = chosen.day;
                            individual[i].StartHour = chosen.start;
                        }
                    }
                }
            }

            var population = new List<List<TaskUnit>>();
            for (int i = 0; i < populationSize; i++)
            {
                var individual = CreateRandomIndividual();
                if (individual != null)
                    population.Add(individual);
            }
            if (population.Count == 0)
                return (false, null);

            for (int gen = 0; gen < maxGenerations; gen++)
            {
                population = population.OrderBy(ind => Fitness(ind)).ToList();
                var best = population[0];
                if (Fitness(best) == 0)
                {
                    return (true, best);
                }
                var nextGen = new List<List<TaskUnit>>
                {
                    population[0], population[1]
                };
                while (nextGen.Count < populationSize)
                {
                    var parent1 = TournamentSelection(population);
                    var parent2 = TournamentSelection(population);
                    var (child1, child2) = Crossover(parent1, parent2);
                    Mutate(child1);
                    Mutate(child2);
                    nextGen.Add(child1);
                    if (nextGen.Count < populationSize)
                        nextGen.Add(child2);
                }
                population = nextGen;
            }
            population = population.OrderBy(ind => Fitness(ind)).ToList();
            if (Fitness(population[0]) == 0)
                return (true, population[0]);
            return (false, null);
        }

        #endregion

        #region Task Ordering and Selection Heuristics

        private List<TaskUnit> OrderTasks(List<TaskUnit> tasks)
        {
            return tasks.OrderBy(t =>
            {
                // Priority: Lab > Embedded Lab > Theory
                int typePriority = t.Kind switch
                {
                    "LAB4" => 0,
                    "EMB_LAB2" => 1,
                    "EMB_TH1" => 2,
                    "TH1" => 3,
                    _ => 4
                };
                return typePriority;
            })
            .ThenBy(t => t.DomainSlots.Count) // MRV (Most Constrained Variable) - deterministic ordering
            .ThenByDescending(t => t.Conflicts.Count) // Degree heuristic
            .ThenBy(t => t.SubjectCode) // Tie-breaker for deterministic ordering
            .ToList();
        }

        private List<(string day, int start)> OrderDomainByLCV(TaskUnit task, List<TaskUnit> allTasks)
        {
            var slotConstraintCount = new Dictionary<(string day, int start), int>();

            foreach (var slot in task.DomainSlots)
            {
                int constraintCount = 0;

                foreach (var otherTask in allTasks)
                {
                    if (otherTask == task || otherTask.IsPlaced) continue;

                    // Count how many slots this assignment would eliminate from other tasks
                    foreach (var otherSlot in otherTask.DomainSlots)
                    {
                        if (WouldConflict(task, slot, otherTask, otherSlot))
                        {
                            constraintCount++;
                        }
                    }
                }

                slotConstraintCount[slot] = constraintCount;
            }

            // Least Constraining Value: choose slots that eliminate fewer options for other tasks
            return slotConstraintCount.OrderBy(kvp => kvp.Value).Select(kvp => kvp.Key).ToList();
        }

        private bool WouldConflict(TaskUnit taskA, (string day, int start) slotA, TaskUnit taskB, (string day, int start) slotB)
        {
            if (slotA.day != slotB.day) return false;

            int startA = slotA.start;
            int endA = startA + taskA.Duration - 1;
            int startB = slotB.start;
            int endB = startB + taskB.Duration - 1;

            if (!(endA >= startB && endB >= startA)) return false;

            var (_, staffA) = SplitStaff(taskA.StaffAssigned);
            var (_, staffB) = SplitStaff(taskB.StaffAssigned);

            if (staffA == staffB) return true;

            if (taskA.IsLab && taskB.IsLab &&
                !string.IsNullOrEmpty(taskA.LabId) && !string.IsNullOrEmpty(taskB.LabId) &&
                taskA.LabId == taskB.LabId)
                return true;

            return false;
        }

        #endregion

        #region Main Backtracking Algorithm

        private async Task<bool> RecursiveBacktracking(List<TaskUnit> tasks,
            string[] days,
            int totalHours,
            Dictionary<string, Dictionary<string, HashSet<int>>> staffOcc,
            Dictionary<string, Dictionary<string, HashSet<int>>> labOcc,
            Dictionary<string, Dictionary<int, string>> timetableGrid,
            int recursionDepth = 0)
        {
            recursionCounter++;
            if (recursionDepth > MAX_RECURSION_DEPTH || recursionCounter > MAX_RECURSION_DEPTH)
                return false;

            // Check if all tasks are placed
            if (tasks.All(t => t.IsPlaced))
                return true;

            // Select next task using simple MRV like original code for better accuracy
            var selectedTask = tasks.Where(t => !t.IsPlaced).OrderBy(t => t.DomainSlots.Count).FirstOrDefault();
            if (selectedTask == null)
                return true;

            // Use randomized domain order like original code
            var orderedDomain = selectedTask.DomainSlots.OrderBy(_ => rng.Next()).ToList();

            // Store domain snapshot before attempting assignments
            var domainSnapshot = tasks.ToDictionary(t => t, t => t.DomainSlots.ToList());

            foreach (var slot in orderedDomain)
            {
                // Validate slot is within bounds and respects constraints
                if (slot.start < 1 || slot.start + selectedTask.Duration - 1 > totalHours)
                    continue;

                // Check if slot is still valid
                if (!IsFreeAndNoConflict(selectedTask, slot.day, slot.start, staffOcc, labOcc, timetableGrid, totalHours))
                    continue;

                // Make assignment
                AssignTask(selectedTask, slot, staffOcc, labOcc, timetableGrid);

                // Propagate constraints
                if (!PropagateConstraints(tasks, selectedTask, slot))
                {
                    UnassignTask(selectedTask, staffOcc, labOcc, timetableGrid);
                    // Restore domains after failed propagation
                    foreach (var kvp in domainSnapshot)
                        kvp.Key.DomainSlots = kvp.Value;
                    continue;
                }

                // Recursive call
                if (await RecursiveBacktracking(tasks, days, totalHours, staffOcc, labOcc, timetableGrid, recursionDepth + 1))
                    return true;

                // Backtrack - unassign task
                UnassignTask(selectedTask, staffOcc, labOcc, timetableGrid);

                // Enhanced conflict handling with rescheduling attempt first
                var conflictComponent = GetConflictComponent(selectedTask);
                if (conflictComponent.Count > 0 && recursionCounter <= MAX_RECURSION_DEPTH)
                {
                    // First try rescheduling approach for small conflicts
                    if (conflictComponent.Count <= 2)
                    {
                        Console.WriteLine($"Attempting rescheduling for small conflict component of size {conflictComponent.Count}");
                        if (TryReschedulingToMakeRoom(selectedTask, tasks, days, totalHours, staffOcc, labOcc, timetableGrid))
                        {
                            // Try again after rescheduling
                        }
                    }
                }
                {
                    // Reset conflict component and try GA fallback like original
                    foreach (var conflictTask in conflictComponent)
                    {
                        if (conflictTask.IsPlaced)
                        {
                            UnassignTask(conflictTask, staffOcc, labOcc, timetableGrid);
                        }
                        conflictTask.IsPlaced = false;
                        conflictTask.DomainSlots.Clear();

                        // Recompute domain like original code
                        foreach (var day in days)
                        {
                            for (int start = 1; start <= totalHours - conflictTask.Duration + 1; start++)
                            {
                                if (IsFreeAndNoConflict(conflictTask, day, start, staffOcc, labOcc, timetableGrid, totalHours))
                                {
                                    conflictTask.DomainSlots.Add((day, start));
                                }
                            }
                        }

                        if (conflictTask.DomainSlots.Count == 0)
                        {
                            // Restore domains and continue with next slot
                            foreach (var kvp in domainSnapshot)
                                kvp.Key.DomainSlots = kvp.Value;
                            break;
                        }
                    }

                    // Try GA fallback like original code
                    if (conflictComponent.Count > 0)
                    {
                        var gaResult = await RunGeneticAlgorithmAsync(conflictComponent, days, totalHours, staffOcc, labOcc);
                        if (gaResult.Succeeded)
                        {
                            // Apply GA solution like original code
                            foreach (var gaTask in gaResult.Result)
                            {
                                var originalTask = conflictComponent.FirstOrDefault(t => t.SubjectCode == gaTask.SubjectCode && t.Kind == gaTask.Kind);
                                if (originalTask != null)
                                {
                                    originalTask.DomainSlots.Clear();
                                    originalTask.DomainSlots.Add((gaTask.Day, gaTask.StartHour));
                                    AssignTask(originalTask, (gaTask.Day, gaTask.StartHour), staffOcc, labOcc, timetableGrid);
                                }
                            }

                            // Rebuild conflict graph and continue like original
                            BuildConflictGraph(tasks);
                            if (await RecursiveBacktracking(tasks, days, totalHours, staffOcc, labOcc, timetableGrid, recursionDepth + 1))
                                return true;

                            // Cleanup if GA solution didn't work
                            foreach (var conflictTask in conflictComponent)
                            {
                                UnassignTask(conflictTask, staffOcc, labOcc, timetableGrid);
                            }
                        }
                        else
                        {
                            // GA failed, clean up conflict tasks like original
                            foreach (var conflictTask in conflictComponent)
                            {
                                UnassignTask(conflictTask, staffOcc, labOcc, timetableGrid);
                            }
                        }
                    }
                }

                // Restore domains for next iteration
                foreach (var kvp in domainSnapshot)
                    kvp.Key.DomainSlots = kvp.Value;
            }

            return false;
        }

        #endregion

        #region Main API Endpoint

        [HttpPost("generateCrossDepartmentTimetableBacktracking")]
        public async Task<IActionResult> GenerateEnhancedTimetable([FromBody] TimetableRequest request)
        {
            if (request == null || request.Subjects == null || request.Subjects.Count == 0)
                return BadRequest(new { message = "❌ Request or subjects missing." });

            var cs = _configuration.GetConnectionString("DefaultConnection");
            await using var conn = new NpgsqlConnection(cs);
            await conn.OpenAsync();

            string[] DAYS = new[] { "Mon", "Tue", "Wed", "Thu", "Fri" };
            int HOURS = Math.Max(1, request.TotalHoursPerDay);

            // Initialize data structures
            var timetableGrid = DAYS.ToDictionary(d => d, d => Enumerable.Range(1, HOURS).ToDictionary(h => h, _ => "---"));
            var staffOcc = new Dictionary<string, Dictionary<string, HashSet<int>>>();
            var labOcc = new Dictionary<string, Dictionary<string, HashSet<int>>>();

            // Load existing occupancy from database
            await LoadExistingOccupancy(conn, staffOcc, labOcc, DAYS);

            // Filter subjects with assigned staff like original code
            var validSubjects = request.Subjects?.Where(s => !string.IsNullOrWhiteSpace(s.StaffAssigned)).ToList();
            if (validSubjects == null || validSubjects.Count == 0)
                return BadRequest(new { message = "❌ No valid subjects with assigned staff found.", receivedPayload = request });

            // Create task units from valid subjects
            var tasks = CreateTaskUnits(validSubjects);
            if (tasks.Count == 0)
                return Ok(new { message = "❌ No valid tasks created from subjects.", receivedPayload = request });

            // Shuffle tasks like original code for randomization
            Shuffle(tasks);

            // Reset recursion counter for this generation attempt
            recursionCounter = 0;

            try
            {
                // Enhanced domain assignment with rescheduling
                if (!AssignDomains(tasks, DAYS, HOURS, staffOcc, labOcc, timetableGrid))
                {
                    return Ok(new { message = $"❌ Unable to find or create available slots for some tasks even after rescheduling attempts.", receivedPayload = request });
                }

                // Build conflict graph like original code
                BuildConflictGraph(tasks);

                // Main backtracking algorithm with integrated GA fallback
                var solved = await RecursiveBacktracking(tasks, DAYS, HOURS, staffOcc, labOcc, timetableGrid);

                if (!solved)
                {
                    return Ok(new { message = "❌ Unsolvable conflict detected", receivedPayload = request });
                }

                // Validate solution with both methods
                if (!ValidateSolution(tasks, HOURS) || !ValidateFinalTimetable(tasks, HOURS))
                {
                    return Ok(new { message = "❌ Timetable validation failed after generation.", receivedPayload = request });
                }

                // Save to database
                await SaveTimetableToDatabase(conn, request, tasks);

                return Ok(new
                {
                    message = "✅ Conflict-free timetable generated successfully with enhanced algorithms.",
                    timetable = timetableGrid.Select(kvp => new { Day = kvp.Key, Slots = kvp.Value }),
                    usedLabIds = tasks.Where(t => t.IsLab && !string.IsNullOrEmpty(t.LabId)).Select(t => t.LabId).Distinct(),
                    taskCount = tasks.Count,
                    placedTasks = tasks.Count(t => t.IsPlaced),
                    receivedPayload = request
                });
            }
            catch (Exception ex)
            {
                return StatusCode(500, new { message = $"❌ Error during timetable generation: {ex.Message}" });
            }
        }

        [HttpGet("generateCrossDepartmentTimetableFromPending")]
        public async Task<IActionResult> GenerateCrossDepartmentTimetableFromPending(
            [FromQuery] string department,
            [FromQuery] string year,
            [FromQuery] string semester,
            [FromQuery] string section)
        {
            if (string.IsNullOrEmpty(department) || string.IsNullOrEmpty(year) ||
                string.IsNullOrEmpty(semester) || string.IsNullOrEmpty(section))
                return BadRequest(new { message = "❌ Department, year, semester, and section are required." });

            var cs = _configuration.GetConnectionString("DefaultConnection");
            await using var conn = new NpgsqlConnection(cs);
            await conn.OpenAsync();

            try
            {
                // Load subjects from pendingtimetabledata table
                var subjects = new List<SubjectDto>();

                var selectQuery = @"
                    SELECT staffname, subject_id, lab_id, staff_department, subject_shrt,
                           credit, subtype, department, year, sem, lab_department, section
                    FROM pendingtimetabledata
                    WHERE department = @dept AND year = @year AND sem = @sem AND section = @section
                    AND status != 'generated'";

                await using (var cmd = new NpgsqlCommand(selectQuery, conn))
                {
                    cmd.Parameters.AddWithValue("dept", department);
                    cmd.Parameters.AddWithValue("year", year);
                    cmd.Parameters.AddWithValue("sem", semester);
                    cmd.Parameters.AddWithValue("section", section);

                    await using var reader = await cmd.ExecuteReaderAsync();
                    while (await reader.ReadAsync())
                    {
                        var subject = new SubjectDto
                        {
                            SubjectCode = reader["subject_id"] as string ?? "",
                            SubjectName = reader["subject_shrt"] as string ?? "",
                            SubjectType = reader["subtype"] as string ?? "theory",
                            Credit = reader["credit"] != DBNull.Value ? (int)reader["credit"] : 1,
                            StaffAssigned = reader["staffname"] as string ?? "",
                            LabId = reader["lab_id"] as string
                        };

                        subjects.Add(subject);
                    }
                }

                if (subjects.Count == 0)
                    return BadRequest(new { message = "❌ No pending subjects found for the specified parameters." });

                Console.WriteLine($"📚 Loaded {subjects.Count} subjects from pending data");

                // Create timetable request object
                var request = new TimetableRequest
                {
                    Department = department,
                    Year = year,
                    Semester = semester,
                    Section = section,
                    TotalHoursPerDay = 7,
                    Subjects = subjects
                };

                string[] DAYS = new[] { "Mon", "Tue", "Wed", "Thu", "Fri" };
                int HOURS = request.TotalHoursPerDay;

                // Initialize data structures
                var timetableGrid = DAYS.ToDictionary(d => d, d => Enumerable.Range(1, HOURS).ToDictionary(h => h, _ => "---"));
                var staffOcc = new Dictionary<string, Dictionary<string, HashSet<int>>>();
                var labOcc = new Dictionary<string, Dictionary<string, HashSet<int>>>();

                // Load existing occupancy from database
                await LoadExistingOccupancy(conn, staffOcc, labOcc, DAYS);

                // Filter subjects with assigned staff
                var validSubjects = subjects.Where(s => !string.IsNullOrWhiteSpace(s.StaffAssigned)).ToList();
                if (validSubjects.Count == 0)
                    return BadRequest(new { message = "❌ No valid subjects with assigned staff found." });

                // Create task units from valid subjects
                var tasks = CreateTaskUnits(validSubjects);
                if (tasks.Count == 0)
                    return BadRequest(new { message = "❌ No valid tasks created from subjects." });

                // Shuffle tasks for randomization
                Shuffle(tasks);

                // Reset recursion counter
                recursionCounter = 0;

                // Build conflict graph
                BuildConflictGraph(tasks);

                // Assign domains to tasks
                if (!AssignDomains(tasks, DAYS, HOURS, staffOcc, labOcc, timetableGrid))
                {
                    return StatusCode(500, new { message = "❌ Failed to assign valid domains to tasks." });
                }

                // Solve using recursive backtracking
                var solved = await RecursiveBacktracking(tasks, DAYS, HOURS, staffOcc, labOcc, timetableGrid);

                if (!solved)
                {
                    return StatusCode(500, new { message = "❌ Could not generate conflict-free timetable with current constraints." });
                }

                // Validate solution
                if (!ValidateSolution(tasks, HOURS) || !ValidateFinalTimetable(tasks, HOURS))
                {
                    return StatusCode(500, new { message = "❌ Generated timetable failed validation." });
                }

                // Save timetable to database
                await SaveTimetableToDatabase(conn, request, tasks);

                // Update status to 'generated' for processed records
                var updateQuery = @"
                    UPDATE pendingtimetabledata 
                    SET status = 'generated' 
                    WHERE department = @dept AND year = @year AND sem = @sem AND section = @section
                    AND status != 'generated'";

                await using (var updateCmd = new NpgsqlCommand(updateQuery, conn))
                {
                    updateCmd.Parameters.AddWithValue("dept", department);
                    updateCmd.Parameters.AddWithValue("year", year);
                    updateCmd.Parameters.AddWithValue("sem", semester);
                    updateCmd.Parameters.AddWithValue("section", section);

                    var updatedRows = await updateCmd.ExecuteNonQueryAsync();
                    Console.WriteLine($"✅ Updated {updatedRows} records to 'generated' status");
                }

                return Ok(new
                {
                    message = "✅ Timetable generated successfully from pending data and status updated.",
                    timetable = timetableGrid.Select(kvp => new { Day = kvp.Key, Slots = kvp.Value }),
                    usedLabIds = tasks.Where(t => t.IsLab && !string.IsNullOrEmpty(t.LabId)).Select(t => t.LabId).Distinct(),
                    taskCount = tasks.Count,
                    placedTasks = tasks.Count(t => t.IsPlaced),
                    processedSubjects = subjects.Count
                });
            }
            catch (Exception ex)
            {
                return StatusCode(500, new { message = $"❌ Error during timetable generation: {ex.Message}" });
            }
        }

        #endregion

        #region Helper Methods for Database and Validation

        private void EnsureDayMap<TKey>(Dictionary<TKey, Dictionary<string, HashSet<int>>> map, TKey key, string[] days)
        {
            if (!map.ContainsKey(key))
                map[key] = days.ToDictionary(d => d, _ => new HashSet<int>());
        }

        private async Task LoadExistingOccupancy(NpgsqlConnection conn,
            Dictionary<string, Dictionary<string, HashSet<int>>> staffOcc,
            Dictionary<string, Dictionary<string, HashSet<int>>> labOcc,
            string[] days)
        {
            // Load staff occupancy from existing classtimetable
            await using (var cmd = new NpgsqlCommand("SELECT staff_code, day, hour FROM classtimetable", conn))
            await using (var reader = await cmd.ExecuteReaderAsync())
            {
                while (await reader.ReadAsync())
                {
                    var staffCode = reader["staff_code"] as string ?? "---";
                    var day = reader["day"] as string ?? "Mon";
                    var hour = (int)reader["hour"];

                    EnsureDayMap(staffOcc, staffCode, days);
                    staffOcc[staffCode][day].Add(hour);
                }
            }

            // Load lab occupancy from existing labtimetable
            await using (var cmd = new NpgsqlCommand("SELECT lab_id, day, hour FROM labtimetable", conn))
            await using (var reader = await cmd.ExecuteReaderAsync())
            {
                while (await reader.ReadAsync())
                {
                    var labId = reader["lab_id"] as string ?? "---";
                    var day = reader["day"] as string ?? "Mon";
                    var hour = (int)reader["hour"];

                    EnsureDayMap(labOcc, labId, days);
                    labOcc[labId][day].Add(hour);
                }
            }
        }

        private List<TaskUnit> CreateTaskUnits(List<SubjectDto> subjects)
        {
            var tasks = new List<TaskUnit>();

            foreach (var subject in subjects.Where(s => !string.IsNullOrWhiteSpace(s.StaffAssigned)))
            {
                var type = (subject.SubjectType ?? "theory").Trim().ToLowerInvariant();

                switch (type)
                {
                    case "lab":
                        tasks.Add(new TaskUnit
                        {
                            SubjectCode = subject.SubjectCode ?? "---",
                            SubjectName = subject.SubjectName ?? "---",
                            StaffAssigned = subject.StaffAssigned,
                            LabId = subject.LabId,
                            IsLab = true,
                            Duration = 4,
                            Kind = "LAB4"
                        });
                        break;

                    case "embedded":
                        tasks.Add(new TaskUnit
                        {
                            SubjectCode = subject.SubjectCode ?? "---",
                            SubjectName = subject.SubjectName ?? "---",
                            StaffAssigned = subject.StaffAssigned,
                            LabId = subject.LabId,
                            IsLab = true,
                            Duration = 2,
                            Kind = "EMB_LAB2"
                        });

                        for (int i = 0; i < 2; i++)
                        {
                            tasks.Add(new TaskUnit
                            {
                                SubjectCode = subject.SubjectCode ?? "---",
                                SubjectName = subject.SubjectName ?? "---",
                                StaffAssigned = subject.StaffAssigned,
                                IsLab = false,
                                Duration = 1,
                                Kind = "EMB_TH1"
                            });
                        }
                        break;

                    default: // theory
                        int creditHours = Math.Max(0, subject.Credit);
                        for (int i = 0; i < creditHours; i++)
                        {
                            tasks.Add(new TaskUnit
                            {
                                SubjectCode = subject.SubjectCode ?? "---",
                                SubjectName = subject.SubjectName ?? "---",
                                StaffAssigned = subject.StaffAssigned,
                                IsLab = false,
                                Duration = 1,
                                Kind = "TH1"
                            });
                        }
                        break;
                }
            }

            return tasks;
        }

        private bool ValidateSolution(List<TaskUnit> tasks, int totalHours)
        {
            var staffSchedule = new Dictionary<string, Dictionary<string, HashSet<int>>>();
            var labSchedule = new Dictionary<string, Dictionary<string, HashSet<int>>>();
            var globalGrid = new Dictionary<string, Dictionary<int, string>>();

            // Initialize global grid for overlap detection
            string[] DAYS = { "Mon", "Tue", "Wed", "Thu", "Fri" };
            foreach (var day in DAYS)
            {
                globalGrid[day] = new Dictionary<int, string>();
                for (int h = 1; h <= totalHours; h++)
                {
                    globalGrid[day][h] = "---";
                }
            }

            foreach (var task in tasks)
            {
                if (!task.IsPlaced)
                {
                    Console.WriteLine($"Validation failed: Task {task.SubjectCode} is not placed");
                    return false;
                }

                var (_, staffCode) = SplitStaff(task.StaffAssigned);

                // Validate basic constraints
                if (string.IsNullOrEmpty(task.Day) || task.StartHour < 1)
                {
                    Console.WriteLine($"Validation failed: Task {task.SubjectCode} has invalid assignment");
                    return false;
                }

                // Initialize staff schedule tracking
                if (!staffSchedule.ContainsKey(staffCode))
                    staffSchedule[staffCode] = new Dictionary<string, HashSet<int>>();
                if (!staffSchedule[staffCode].ContainsKey(task.Day))
                    staffSchedule[staffCode][task.Day] = new HashSet<int>();

                // Initialize lab schedule tracking
                if (task.IsLab && !string.IsNullOrEmpty(task.LabId))
                {
                    if (!labSchedule.ContainsKey(task.LabId))
                        labSchedule[task.LabId] = new Dictionary<string, HashSet<int>>();
                    if (!labSchedule[task.LabId].ContainsKey(task.Day))
                        labSchedule[task.LabId][task.Day] = new HashSet<int>();
                }

                // Check each hour of the task
                for (int h = task.StartHour; h < task.StartHour + task.Duration; h++)
                {
                    // Validate time bounds (1-based indexing)
                    if (h < 1 || h > totalHours)
                    {
                        Console.WriteLine($"Validation failed: Task {task.SubjectCode} hour {h} is out of bounds");
                        return false;
                    }

                    // Check global grid conflicts
                    if (globalGrid[task.Day][h] != "---")
                    {
                        Console.WriteLine($"Validation failed: Global conflict at {task.Day} hour {h}");
                        return false;
                    }
                    globalGrid[task.Day][h] = task.SubjectCode;

                    // Check staff conflicts
                    if (staffSchedule[staffCode][task.Day].Contains(h))
                    {
                        Console.WriteLine($"Validation failed: Staff {staffCode} conflict at {task.Day} hour {h}");
                        return false;
                    }
                    staffSchedule[staffCode][task.Day].Add(h);

                    // Check lab conflicts
                    if (task.IsLab && !string.IsNullOrEmpty(task.LabId))
                    {
                        if (labSchedule[task.LabId][task.Day].Contains(h))
                        {
                            Console.WriteLine($"Validation failed: Lab {task.LabId} conflict at {task.Day} hour {h}");
                            return false;
                        }
                        labSchedule[task.LabId][task.Day].Add(h);
                    }
                }

                // Validate lab-specific constraints: 4-hour labs must start at hour 1 or 4
                if (task.IsLab && task.Duration == 4 && !(task.StartHour == 1 || task.StartHour == 4))
                {
                    Console.WriteLine($"Validation failed: Lab {task.SubjectCode} starts at invalid hour {task.StartHour}");
                    return false;
                }

                // Validate duration constraints
                if (task.StartHour + task.Duration - 1 > totalHours)
                {
                    Console.WriteLine($"Validation failed: Task {task.SubjectCode} duration exceeds day limits");
                    return false;
                }
            }

            Console.WriteLine("Solution validation passed successfully");
            return true;
        }

        private bool ValidateFinalTimetable(List<TaskUnit> tasks, int totalHours)
        {
            var staffSchedule = new Dictionary<string, Dictionary<string, HashSet<int>>>();
            var labSchedule = new Dictionary<string, Dictionary<string, HashSet<int>>>();

            foreach (var task in tasks)
            {
                if (!task.IsPlaced)
                {
                    Console.WriteLine($"❌ Task {task.SubjectCode} is not placed");
                    return false;
                }

                var (_, staffCode) = SplitStaff(task.StaffAssigned);

                if (!staffSchedule.ContainsKey(staffCode))
                    staffSchedule[staffCode] = new Dictionary<string, HashSet<int>>();
                if (!staffSchedule[staffCode].ContainsKey(task.Day))
                    staffSchedule[staffCode][task.Day] = new HashSet<int>();

                if (task.IsLab && !string.IsNullOrEmpty(task.LabId))
                {
                    if (!labSchedule.ContainsKey(task.LabId))
                        labSchedule[task.LabId] = new Dictionary<string, HashSet<int>>();
                    if (!labSchedule[task.LabId].ContainsKey(task.Day))
                        labSchedule[task.LabId][task.Day] = new HashSet<int>();
                }

                // Validate hour bounds and conflicts
                for (int h = task.StartHour; h < task.StartHour + task.Duration; h++)
                {
                    if (h < 1 || h > totalHours)
                    {
                        Console.WriteLine($"❌ Task {task.SubjectCode} has invalid hours: {task.StartHour}-{task.StartHour + task.Duration - 1}");
                        return false;
                    }

                    // Check staff conflicts within this generation
                    if (staffSchedule[staffCode][task.Day].Contains(h))
                    {
                        Console.WriteLine($"❌ Staff conflict detected for {staffCode} on {task.Day} at hour {h}");
                        return false;
                    }
                    staffSchedule[staffCode][task.Day].Add(h);

                    // Check lab conflicts within this generation
                    if (task.IsLab && !string.IsNullOrEmpty(task.LabId))
                    {
                        if (labSchedule[task.LabId][task.Day].Contains(h))
                        {
                            Console.WriteLine($"❌ Lab conflict detected for {task.LabId} on {task.Day} at hour {h}");
                            return false;
                        }
                        labSchedule[task.LabId][task.Day].Add(h);
                    }
                }

                // Validate lab-specific constraints
                if (task.IsLab && task.Duration == 4 && !(task.StartHour == 1 || task.StartHour == 4))
                {
                    Console.WriteLine($"❌ 4-hour lab {task.SubjectCode} has invalid start hour: {task.StartHour}");
                    return false;
                }
            }

            // Check for global slot conflicts (no two tasks at same time slot)
            var globalSlotMap = new Dictionary<(string Day, int Hour), TaskUnit>();
            foreach (var task in tasks)
            {
                for (int h = task.StartHour; h < task.StartHour + task.Duration; h++)
                {
                    var key = (task.Day, h);
                    if (globalSlotMap.ContainsKey(key))
                    {
                        Console.WriteLine($"❌ Global slot conflict at {task.Day} hour {h} between {globalSlotMap[key].SubjectCode} and {task.SubjectCode}");
                        return false;
                    }
                    globalSlotMap[key] = task;
                }
            }

            return true;
        }

        private async Task SaveTimetableToDatabase(NpgsqlConnection conn, TimetableRequest request, List<TaskUnit> tasks)
        {
            await using var transaction = await conn.BeginTransactionAsync();

            // Delete existing entries for this department/year/semester/section
            await using (var deleteCmd = new NpgsqlCommand(@"
                DELETE FROM classtimetable 
                WHERE department_id = @dept AND year = @year AND semester = @sem AND section = @section;
                DELETE FROM labtimetable 
                WHERE department = @dept AND year = @year AND semester = @sem AND section = @section;",
                conn, transaction))
            {
                deleteCmd.Parameters.AddWithValue("dept", request.Department ?? "---");
                deleteCmd.Parameters.AddWithValue("year", request.Year ?? "---");
                deleteCmd.Parameters.AddWithValue("sem", request.Semester ?? "---");
                deleteCmd.Parameters.AddWithValue("section", request.Section ?? "---");
                await deleteCmd.ExecuteNonQueryAsync();
            }

            // Insert new timetable entries
            foreach (var task in tasks.Where(t => t.IsPlaced))
            {
                var (staffName, staffCode) = SplitStaff(task.StaffAssigned);

                // Insert into classtimetable for all hours
                for (int h = task.StartHour; h < task.StartHour + task.Duration; h++)
                {
                    await using (var classCmd = new NpgsqlCommand(@"
                        INSERT INTO classtimetable 
                        (staff_name, staff_code, department_id, year, semester, section, day, hour, subject_code, subject_name)
                        VALUES (@staff_name, @staff_code, @dept, @year, @sem, @section, @day, @hour, @sub_code, @sub_name)",
                        conn, transaction))
                    {
                        classCmd.Parameters.AddWithValue("staff_name", staffName);
                        classCmd.Parameters.AddWithValue("staff_code", staffCode);
                        classCmd.Parameters.AddWithValue("dept", request.Department ?? "---");
                        classCmd.Parameters.AddWithValue("year", request.Year ?? "---");
                        classCmd.Parameters.AddWithValue("sem", request.Semester ?? "---");
                        classCmd.Parameters.AddWithValue("section", request.Section ?? "---");
                        classCmd.Parameters.AddWithValue("day", task.Day);
                        classCmd.Parameters.AddWithValue("hour", h);
                        classCmd.Parameters.AddWithValue("sub_code", task.SubjectCode);
                        classCmd.Parameters.AddWithValue("sub_name", task.SubjectName);
                        await classCmd.ExecuteNonQueryAsync();
                    }
                }

                // Insert into labtimetable for lab subjects with special hour mapping
                if (task.IsLab && !string.IsNullOrEmpty(task.LabId))
                {
                    // For 4-hour labs: if starting at hour 4 (afternoon), lab timetable shows 5,6,7
                    // For other start times, lab timetable matches class timetable
                    int labStartHour = task.StartHour == 4 ? 5 : task.StartHour;

                    for (int h = labStartHour; h < task.StartHour + task.Duration; h++)
                    {
                        await using (var labCmd = new NpgsqlCommand(@"
                            INSERT INTO labtimetable 
                            (lab_id, subject_code, subject_name, staff_name, department, year, semester, section, day, hour)
                            VALUES (@lab_id, @sub_code, @sub_name, @staff_name, @dept, @year, @sem, @section, @day, @hour)",
                            conn, transaction))
                        {
                            labCmd.Parameters.AddWithValue("lab_id", task.LabId);
                            labCmd.Parameters.AddWithValue("sub_code", task.SubjectCode);
                            labCmd.Parameters.AddWithValue("sub_name", task.SubjectName);
                            labCmd.Parameters.AddWithValue("staff_name", staffName);
                            labCmd.Parameters.AddWithValue("dept", request.Department ?? "---");
                            labCmd.Parameters.AddWithValue("year", request.Year ?? "---");
                            labCmd.Parameters.AddWithValue("sem", request.Semester ?? "---");
                            labCmd.Parameters.AddWithValue("section", request.Section ?? "---");
                            labCmd.Parameters.AddWithValue("day", task.Day);
                            labCmd.Parameters.AddWithValue("hour", h);
                            await labCmd.ExecuteNonQueryAsync();
                        }
                    }
                }
            }

            await transaction.CommitAsync();
        }

        #endregion
    }
}