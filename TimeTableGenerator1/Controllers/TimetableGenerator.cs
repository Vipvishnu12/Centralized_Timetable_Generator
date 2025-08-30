
//new waste code
using System;
using System.Collections.Generic;
using System.Linq;

namespace TimetableGA
{
    public class TimetableEngine
    {
        private static readonly string[] Days = { "Mon", "Tue", "Wed", "Thu", "Fri" };
        private const int HoursPerDay = 7;
        private readonly Random random = new();

        // Define lab blocks in hours
        private readonly (int start, int duration) MorningLabBlock = (1, 4);  // Hours 1-4
        private readonly (int start, int duration) AfternoonLabBlock = (5, 3); // Hours 5-7

        // Backtracking configuration
        public int MaxBacktrackDepth = 100;
        public int BacktrackRetries = 5;

        public class Subject
        {
            public string SubjectCode { get; set; }
            public string SubjectName { get; set; }
            public string SubjectType { get; set; } // "Theory", "Lab", "Embedded"
            public int Credit { get; set; }
            public string StaffAssigned { get; set; }
            public string LabId { get; set; }
            public int Priority { get; set; } = 1; // Higher priority subjects get scheduled first
        }

        public class TimetableSlot
        {
            public string Day { get; set; }
            public Dictionary<int, string> HourlySlots { get; set; } = new();
        }

        public class Gene
        {
            public string SubjectCode;
            public string StaffAssigned;
            public string LabId;
            public string Day;
            public int StartHour;
            public int Duration;
            public bool IsLabBlock;
            public int AssignmentOrder; // For backtracking priority
            public List<(string day, int hour)> TriedSlots = new(); // Track failed attempts
        }

        public class Chromosome
        {
            public List<Gene> Genes = new List<Gene>();
            public int FitnessScore;
            public Dictionary<string, HashSet<(string day, int hour)>> StaffOccupancy = new();
            public Dictionary<string, HashSet<(string day, int hour)>> LabOccupancy = new();
        }

        public class BacktrackState
        {
            public List<Gene> AssignedGenes = new();
            public Dictionary<string, HashSet<(string day, int hour)>> StaffOccupancy = new();
            public Dictionary<string, HashSet<(string day, int hour)>> LabOccupancy = new();
            public int ConflictCount = 0;
        }

        private List<Subject> Subjects;
        private Dictionary<string, Dictionary<string, HashSet<int>>> StaffAvailability;
        private Dictionary<string, Dictionary<string, HashSet<int>>> LabAvailability;

        public int PopulationSize = 100;
        public int MaxGenerations = 300;
        public double MutationRate = 0.1;

        public void Initialize(List<Subject> subjects,
            Dictionary<string, Dictionary<string, HashSet<int>>> staffAvailability,
            Dictionary<string, Dictionary<string, HashSet<int>>> labAvailability)
        {
            Subjects = subjects.OrderByDescending(s => s.Priority).ThenBy(s => s.SubjectType == "Lab" ? 0 : 1).ToList();
            StaffAvailability = staffAvailability ?? new Dictionary<string, Dictionary<string, HashSet<int>>>();
            LabAvailability = labAvailability ?? new Dictionary<string, Dictionary<string, HashSet<int>>>();
        }

        private List<Gene> CreateGenesForSubject(Subject subject)
        {
            var genes = new List<Gene>();
            var type = subject.SubjectType?.ToLower() ?? "theory";

            if (type == "lab")
            {
                genes.Add(new Gene
                {
                    SubjectCode = subject.SubjectCode,
                    StaffAssigned = subject.StaffAssigned,
                    LabId = subject.LabId,
                    Duration = MorningLabBlock.duration, // 4 hours
                    IsLabBlock = true
                });
            }
            else if (type == "embedded")
            {
                // Theory hours
                for (int i = 0; i < 2; i++)
                {
                    genes.Add(new Gene
                    {
                        SubjectCode = subject.SubjectCode,
                        StaffAssigned = subject.StaffAssigned,
                        LabId = null,
                        Duration = 1,
                        IsLabBlock = false
                    });
                }

                // Lab hours
                genes.Add(new Gene
                {
                    SubjectCode = subject.SubjectCode,
                    StaffAssigned = subject.StaffAssigned,
                    LabId = subject.LabId,
                    Duration = 2,
                    IsLabBlock = true
                });
            }
            else
            {
                // Theory subjects
                for (int i = 0; i < subject.Credit; i++)
                {
                    genes.Add(new Gene
                    {
                        SubjectCode = subject.SubjectCode,
                        StaffAssigned = subject.StaffAssigned,
                        LabId = null,
                        Duration = 1,
                        IsLabBlock = false
                    });
                }
            }

            return genes;
        }

        private List<(string day, int startHour)> GetValidTimeSlots(Gene gene)
        {
            var validSlots = new List<(string day, int startHour)>();

            foreach (var day in Days)
            {
                if (gene.IsLabBlock)
                {
                    if (gene.Duration == 4)
                    {
                        // 4-hour labs only in morning block
                        validSlots.Add((day, MorningLabBlock.start));
                    }
                    else if (gene.Duration == 3)
                    {
                        // 3-hour labs only in afternoon block
                        validSlots.Add((day, AfternoonLabBlock.start));
                    }
                    else if (gene.Duration == 2)
                    {
                        // 2-hour embedded labs can be in either block
                        for (int h = MorningLabBlock.start; h <= MorningLabBlock.start + MorningLabBlock.duration - gene.Duration; h++)
                        {
                            validSlots.Add((day, h));
                        }
                        for (int h = AfternoonLabBlock.start; h <= AfternoonLabBlock.start + AfternoonLabBlock.duration - gene.Duration; h++)
                        {
                            validSlots.Add((day, h));
                        }
                    }
                }
                else
                {
                    // Theory subjects can be anywhere
                    for (int h = 1; h <= HoursPerDay; h++)
                    {
                        validSlots.Add((day, h));
                    }
                }
            }

            // Filter out tried slots
            return validSlots.Where(slot => !gene.TriedSlots.Contains(slot)).ToList();
        }

        private bool IsSlotAvailable(Gene gene, string day, int startHour, BacktrackState state)
        {
            // Check if all required hours are within valid time blocks
            for (int h = startHour; h < startHour + gene.Duration; h++)
            {
                if (h > HoursPerDay) return false;

                // Check lab block constraints
                if (gene.IsLabBlock)
                {
                    if (gene.Duration == 4 && (h < MorningLabBlock.start || h >= MorningLabBlock.start + MorningLabBlock.duration))
                        return false;
                    if (gene.Duration == 3 && (h < AfternoonLabBlock.start || h >= AfternoonLabBlock.start + AfternoonLabBlock.duration))
                        return false;
                    if (gene.Duration == 2)
                    {
                        bool inMorning = h >= MorningLabBlock.start && h < MorningLabBlock.start + MorningLabBlock.duration;
                        bool inAfternoon = h >= AfternoonLabBlock.start && h < AfternoonLabBlock.start + AfternoonLabBlock.duration;
                        if (!inMorning && !inAfternoon) return false;
                    }
                }

                // Check staff availability
                if (StaffAvailability.ContainsKey(gene.StaffAssigned) &&
                    StaffAvailability[gene.StaffAssigned].ContainsKey(day) &&
                    StaffAvailability[gene.StaffAssigned][day].Contains(h))
                {
                    return false;
                }

                // Check staff conflicts
                if (state.StaffOccupancy.ContainsKey(gene.StaffAssigned) &&
                    state.StaffOccupancy[gene.StaffAssigned].Contains((day, h)))
                {
                    return false;
                }

                // Check lab availability and conflicts
                if (!string.IsNullOrEmpty(gene.LabId))
                {
                    if (LabAvailability.ContainsKey(gene.LabId) &&
                        LabAvailability[gene.LabId].ContainsKey(day) &&
                        LabAvailability[gene.LabId][day].Contains(h))
                    {
                        return false;
                    }

                    if (state.LabOccupancy.ContainsKey(gene.LabId) &&
                        state.LabOccupancy[gene.LabId].Contains((day, h)))
                    {
                        return false;
                    }
                }
            }

            return true;
        }

        private void AssignGeneToSlot(Gene gene, string day, int startHour, BacktrackState state)
        {
            gene.Day = day;
            gene.StartHour = startHour;

            // Update occupancy
            if (!state.StaffOccupancy.ContainsKey(gene.StaffAssigned))
                state.StaffOccupancy[gene.StaffAssigned] = new HashSet<(string, int)>();

            if (!string.IsNullOrEmpty(gene.LabId) && !state.LabOccupancy.ContainsKey(gene.LabId))
                state.LabOccupancy[gene.LabId] = new HashSet<(string, int)>();

            for (int h = startHour; h < startHour + gene.Duration; h++)
            {
                state.StaffOccupancy[gene.StaffAssigned].Add((day, h));
                if (!string.IsNullOrEmpty(gene.LabId))
                    state.LabOccupancy[gene.LabId].Add((day, h));
            }

            state.AssignedGenes.Add(gene);
        }

        private void UnassignGene(Gene gene, BacktrackState state)
        {
            // Remove from occupancy
            for (int h = gene.StartHour; h < gene.StartHour + gene.Duration; h++)
            {
                state.StaffOccupancy[gene.StaffAssigned].Remove((gene.Day, h));
                if (!string.IsNullOrEmpty(gene.LabId))
                    state.LabOccupancy[gene.LabId].Remove((gene.Day, h));
            }

            // Mark this slot as tried
            gene.TriedSlots.Add((gene.Day, gene.StartHour));

            state.AssignedGenes.Remove(gene);
        }

        private bool BacktrackSchedule(List<Gene> allGenes, int geneIndex, BacktrackState state, int depth = 0)
        {
            if (depth > MaxBacktrackDepth) return false;
            if (geneIndex >= allGenes.Count) return true; // All genes assigned successfully

            var currentGene = allGenes[geneIndex];
            var validSlots = GetValidTimeSlots(currentGene);

            // Shuffle for randomness but prioritize better slots
            validSlots = validSlots.OrderBy(slot => random.Next()).ToList();

            foreach (var (day, startHour) in validSlots)
            {
                if (IsSlotAvailable(currentGene, day, startHour, state))
                {
                    AssignGeneToSlot(currentGene, day, startHour, state);

                    // Recursively try to assign remaining genes
                    if (BacktrackSchedule(allGenes, geneIndex + 1, state, depth + 1))
                    {
                        return true; // Successfully assigned all remaining genes
                    }

                    // Backtrack: unassign current gene and try next slot
                    UnassignGene(currentGene, state);
                }
            }

            // If we reach here, no valid slot found for current gene
            // Try to resolve conflicts by backtracking previously assigned genes
            if (geneIndex > 0 && depth < MaxBacktrackDepth)
            {
                // Find conflicting genes and try to reassign them
                var conflictingGenes = FindConflictingGenes(currentGene, state);

                foreach (var conflictingGene in conflictingGenes.Take(3)) // Limit backtrack scope
                {
                    // Temporarily unassign conflicting gene
                    UnassignGene(conflictingGene, state);

                    // Try to assign current gene
                    var newValidSlots = GetValidTimeSlots(currentGene);
                    foreach (var (day, startHour) in newValidSlots)
                    {
                        if (IsSlotAvailable(currentGene, day, startHour, state))
                        {
                            AssignGeneToSlot(currentGene, day, startHour, state);

                            // Try to reassign the conflicting gene
                            var conflictingSlots = GetValidTimeSlots(conflictingGene);
                            bool conflictingReassigned = false;

                            foreach (var (cDay, cStartHour) in conflictingSlots)
                            {
                                if (IsSlotAvailable(conflictingGene, cDay, cStartHour, state))
                                {
                                    AssignGeneToSlot(conflictingGene, cDay, cStartHour, state);
                                    conflictingReassigned = true;
                                    break;
                                }
                            }

                            if (conflictingReassigned)
                            {
                                // Continue with next gene
                                if (BacktrackSchedule(allGenes, geneIndex + 1, state, depth + 1))
                                {
                                    return true;
                                }

                                // If that didn't work, unassign both and continue
                                UnassignGene(conflictingGene, state);
                            }

                            UnassignGene(currentGene, state);
                        }
                    }

                    // Reassign conflicting gene to its original position if possible
                    var originalSlots = GetValidTimeSlots(conflictingGene);
                    foreach (var (oDay, oStartHour) in originalSlots)
                    {
                        if (IsSlotAvailable(conflictingGene, oDay, oStartHour, state))
                        {
                            AssignGeneToSlot(conflictingGene, oDay, oStartHour, state);
                            break;
                        }
                    }
                }
            }

            return false; // Could not assign current gene
        }

        private List<Gene> FindConflictingGenes(Gene currentGene, BacktrackState state)
        {
            var conflicting = new List<Gene>();

            foreach (var assignedGene in state.AssignedGenes)
            {
                // Check staff conflicts
                if (assignedGene.StaffAssigned == currentGene.StaffAssigned)
                {
                    conflicting.Add(assignedGene);
                }

                // Check lab conflicts
                if (!string.IsNullOrEmpty(currentGene.LabId) &&
                    currentGene.LabId == assignedGene.LabId)
                {
                    conflicting.Add(assignedGene);
                }
            }

            return conflicting.Distinct().OrderBy(g => g.AssignmentOrder).ToList();
        }

        private Chromosome CreateChromosomeWithBacktracking()
        {
            var chromosome = new Chromosome();
            var allGenes = new List<Gene>();

            // Create all genes
            int assignmentOrder = 0;
            foreach (var subject in Subjects)
            {
                var subjectGenes = CreateGenesForSubject(subject);
                foreach (var gene in subjectGenes)
                {
                    gene.AssignmentOrder = assignmentOrder++;
                    allGenes.Add(gene);
                }
            }

            // Try multiple times with different orderings
            for (int attempt = 0; attempt < BacktrackRetries; attempt++)
            {
                // Reset all genes
                foreach (var gene in allGenes)
                {
                    gene.TriedSlots.Clear();
                    gene.Day = null;
                    gene.StartHour = 0;
                }

                var state = new BacktrackState();

                // Randomize order slightly while respecting priorities
                var shuffledGenes = allGenes
                    .OrderBy(g => Subjects.First(s => s.SubjectCode == g.SubjectCode).Priority)
                    .ThenBy(g => g.IsLabBlock ? 0 : 1) // Labs first
                    .ThenBy(g => random.Next())
                    .ToList();

                if (BacktrackSchedule(shuffledGenes, 0, state))
                {
                    chromosome.Genes = new List<Gene>(allGenes);
                    chromosome.StaffOccupancy = state.StaffOccupancy;
                    chromosome.LabOccupancy = state.LabOccupancy;
                    chromosome.FitnessScore = CalculateAdvancedFitness(chromosome);
                    return chromosome;
                }
            }

            // If backtracking failed, create a partial schedule
            chromosome.Genes = allGenes.Where(g => !string.IsNullOrEmpty(g.Day)).ToList();
            chromosome.FitnessScore = CalculateAdvancedFitness(chromosome);
            return chromosome;
        }

        private Chromosome CreateRandomChromosome()
        {
            // First try backtracking approach
            var backtrackResult = CreateChromosomeWithBacktracking();
            if (backtrackResult.FitnessScore >= 0) // No major conflicts
            {
                return backtrackResult;
            }

            // Fallback to original random approach
            var chromosome = new Chromosome();

            foreach (var subject in Subjects)
            {
                var genes = CreateGenesForSubject(subject);
                foreach (var gene in genes)
                {
                    var validSlots = GetValidTimeSlots(gene);
                    if (validSlots.Count > 0)
                    {
                        var (day, startHour) = validSlots[random.Next(validSlots.Count)];
                        gene.Day = day;
                        gene.StartHour = startHour;
                    }
                    else
                    {
                        // Assign randomly as fallback
                        gene.Day = Days[random.Next(Days.Length)];
                        gene.StartHour = random.Next(1, HoursPerDay + 1);
                    }
                    chromosome.Genes.Add(gene);
                }
            }

            chromosome.FitnessScore = CalculateAdvancedFitness(chromosome);
            return chromosome;
        }

        private int CalculateAdvancedFitness(Chromosome chromosome)
        {
            int score = 0;
            var staffSchedule = new Dictionary<string, Dictionary<string, HashSet<int>>>();
            var labSchedule = new Dictionary<string, Dictionary<string, HashSet<int>>>();

            foreach (var gene in chromosome.Genes)
            {
                if (string.IsNullOrEmpty(gene.Day))
                {
                    score -= 50; // Unassigned gene penalty
                    continue;
                }

                if (!staffSchedule.ContainsKey(gene.StaffAssigned))
                    staffSchedule[gene.StaffAssigned] = Days.ToDictionary(d => d, d => new HashSet<int>());

                if (!string.IsNullOrWhiteSpace(gene.LabId) && !labSchedule.ContainsKey(gene.LabId))
                    labSchedule[gene.LabId] = Days.ToDictionary(d => d, d => new HashSet<int>());

                for (int hour = gene.StartHour; hour < gene.StartHour + gene.Duration; hour++)
                {
                    if (hour > HoursPerDay)
                    {
                        score -= 25; // Out of bounds penalty
                        continue;
                    }

                    // Lab block constraint validation
                    if (gene.IsLabBlock)
                    {
                        bool validLabHour = false;
                        if (gene.Duration == 4)
                            validLabHour = hour >= MorningLabBlock.start && hour < MorningLabBlock.start + MorningLabBlock.duration;
                        else if (gene.Duration == 3)
                            validLabHour = hour >= AfternoonLabBlock.start && hour < AfternoonLabBlock.start + AfternoonLabBlock.duration;
                        else if (gene.Duration == 2)
                            validLabHour = (hour >= MorningLabBlock.start && hour < MorningLabBlock.start + MorningLabBlock.duration) ||
                                          (hour >= AfternoonLabBlock.start && hour < AfternoonLabBlock.start + AfternoonLabBlock.duration);

                        if (!validLabHour) score -= 20;
                    }

                    // Staff availability check
                    if (StaffAvailability.ContainsKey(gene.StaffAssigned) &&
                        StaffAvailability[gene.StaffAssigned].ContainsKey(gene.Day) &&
                        StaffAvailability[gene.StaffAssigned][gene.Day].Contains(hour))
                    {
                        score -= 15; // Staff unavailable penalty
                    }

                    // Staff conflict check
                    if (staffSchedule[gene.StaffAssigned][gene.Day].Contains(hour))
                        score -= 10; // Staff double booking
                    else
                        staffSchedule[gene.StaffAssigned][gene.Day].Add(hour);

                    // Lab availability and conflict check
                    if (!string.IsNullOrWhiteSpace(gene.LabId))
                    {
                        if (LabAvailability.ContainsKey(gene.LabId) &&
                            LabAvailability[gene.LabId].ContainsKey(gene.Day) &&
                            LabAvailability[gene.LabId][gene.Day].Contains(hour))
                        {
                            score -= 15; // Lab unavailable penalty
                        }

                        if (labSchedule[gene.LabId][gene.Day].Contains(hour))
                            score -= 10; // Lab double booking
                        else
                            labSchedule[gene.LabId][gene.Day].Add(hour);
                    }
                }

                // Bonus for properly scheduled genes
                if (score >= -5) score += 1;
            }

            return score;
        }

        private int CalculateFitness(Chromosome chromosome)
        {
            return CalculateAdvancedFitness(chromosome);
        }

        private Chromosome TournamentSelection(List<Chromosome> population)
        {
            int tournamentSize = 5;
            var candidates = new List<Chromosome>();
            for (int i = 0; i < tournamentSize; i++)
                candidates.Add(population[random.Next(population.Count)]);
            return candidates.OrderByDescending(c => c.FitnessScore).First();
        }

        private (Chromosome, Chromosome) Crossover(Chromosome p1, Chromosome p2)
        {
            int cut = random.Next(1, Math.Min(p1.Genes.Count, p2.Genes.Count) - 1);
            var c1Genes = p1.Genes.Take(cut).Concat(p2.Genes.Skip(cut)).ToList();
            var c2Genes = p2.Genes.Take(cut).Concat(p1.Genes.Skip(cut)).ToList();
            return (new Chromosome { Genes = c1Genes }, new Chromosome { Genes = c2Genes });
        }

        private void Mutate(Chromosome chromosome)
        {
            foreach (var gene in chromosome.Genes)
            {
                if (random.NextDouble() < MutationRate)
                {
                    var validSlots = GetValidTimeSlots(gene);
                    if (validSlots.Count > 0)
                    {
                        var (day, startHour) = validSlots[random.Next(validSlots.Count)];
                        gene.Day = day;
                        gene.StartHour = startHour;
                    }
                }
            }
        }

        public (List<TimetableSlot> timetable, List<Conflict> conflicts, Chromosome bestChromosome) GenerateGA()
        {
            var population = new List<Chromosome>();

            // Create initial population with backtracking
            for (int i = 0; i < PopulationSize; i++)
            {
                var chrom = CreateRandomChromosome();
                population.Add(chrom);
            }

            Chromosome bestOverall = population.OrderByDescending(c => c.FitnessScore).First();

            for (int gen = 0; gen < MaxGenerations; gen++)
            {
                var nextPop = new List<Chromosome>();

                // Keep best chromosome (elitism)
                nextPop.Add(bestOverall);

                while (nextPop.Count < PopulationSize)
                {
                    var p1 = TournamentSelection(population);
                    var p2 = TournamentSelection(population);

                    var (c1, c2) = Crossover(p1, p2);

                    Mutate(c1);
                    Mutate(c2);

                    c1.FitnessScore = CalculateFitness(c1);
                    c2.FitnessScore = CalculateFitness(c2);

                    nextPop.Add(c1);
                    if (nextPop.Count < PopulationSize)
                        nextPop.Add(c2);
                }

                population = nextPop.OrderByDescending(c => c.FitnessScore).Take(PopulationSize).ToList();

                var currentBest = population[0];
                if (currentBest.FitnessScore > bestOverall.FitnessScore)
                    bestOverall = currentBest;

                // Early termination if perfect solution found
                if (bestOverall.FitnessScore >= 0 &&
                    bestOverall.Genes.All(g => !string.IsNullOrEmpty(g.Day)))
                    break;
            }

            return (ConvertToTimetable(bestOverall.Genes), ExtractConflicts(bestOverall), bestOverall);
        }

        private List<TimetableSlot> ConvertToTimetable(List<Gene> genes)
        {
            var timetable = Days.Select(day => new TimetableSlot
            {
                Day = day,
                HourlySlots = Enumerable.Range(1, HoursPerDay).ToDictionary(h => h, _ => "---")
            }).ToList();

            foreach (var gene in genes)
            {
                if (string.IsNullOrEmpty(gene.Day)) continue;

                var slot = timetable.FirstOrDefault(t => t.Day == gene.Day);
                if (slot != null)
                {
                    for (int h = gene.StartHour; h < gene.StartHour + gene.Duration; h++)
                    {
                        if (h <= HoursPerDay)
                            slot.HourlySlots[h] = $"{gene.SubjectCode} ({gene.StaffAssigned})";
                    }
                }
            }

            return timetable;
        }

        private List<Conflict> ExtractConflicts(Chromosome chromosome)
        {
            var conflicts = new List<Conflict>();

            if (chromosome.FitnessScore < 0)
            {
                conflicts.Add(new Conflict
                {
                    Reason = $"Schedule contains conflicts (fitness score: {chromosome.FitnessScore}). Staff or lab double bookings or unavailability issues detected."
                });
            }

            var unscheduledGenes = chromosome.Genes.Where(g => string.IsNullOrEmpty(g.Day)).ToList();
            if (unscheduledGenes.Count > 0)
            {
                conflicts.Add(new Conflict
                {
                    Reason = $"{unscheduledGenes.Count} subjects could not be scheduled: {string.Join(", ", unscheduledGenes.Select(g => g.SubjectCode).Distinct())}"
                });
            }

            return conflicts;
        }

        public class Conflict
        {
            public Subject Subject { get; set; } = null;
            public string Reason { get; set; }
        }
    }
}

