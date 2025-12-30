

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