using Microsoft.EntityFrameworkCore;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
namespace TimetableGenerator
{
    public class Admin
    {
        [Column("department_id")]
        [MaxLength(20)]
        public string DepartmentId { get; set; }
        [Column("department_name")]
        [MaxLength(100)]
        public string DepartmentName { get; set; }
        public Login Login { get; set; }
        public ICollection<SubjectData> Subjects { get; set; }
        public ICollection<StaffData> Staffs { get; set; }
        public ICollection<LabData> Labs { get; set; }
    }
    public class Login
    {
        [Column("username")]
        [MaxLength(20)]
        [ForeignKey(nameof(Admin))]
        public string Username { get; set; }
        [Column("password")]
        [MaxLength(100)]
        public string Password { get; set; }
        [Column("role")]
        [MaxLength(20)]
        public string Role { get; set; }
        public Admin Admin { get; set; }
    }
    public class SubjectData
    {
        [Column("subject_code")]
        [MaxLength(20)]
        public string SubjectCode { get; set; }
        [Column("subject_shortform")]
        [MaxLength(20)]
        public string SubjectShortform { get; set; }
        [Column("subject_name")]
        [MaxLength(100)]
        public string SubjectName { get; set; }
        public int? Credit { get; set; }
        public string Year { get; set; }
        public string Sem { get; set; }
        [Column("subject_type")]
        [MaxLength(20)]
        public string SubjectType { get; set; }
        [Column("department_id")]
        [MaxLength(20)]
        public string DepartmentId { get; set; }
        [ForeignKey("DepartmentId")]
        public Admin Department { get; set; }
    }
    public class StaffData
    {
        [Column("staffid")]
        [MaxLength(20)]
        public string StaffId { get; set; }
        [Column("staffname")]
        [MaxLength(100)]
        public string StaffName { get; set; }
        [MaxLength(20)]
        public string PrefSub1 { get; set; }
        [MaxLength(20)]
        public string PrefSub2 { get; set; }
        [MaxLength(20)]
        public string PrefSub3 { get; set; }
        [Column("departmentid")]
        [MaxLength(20)]
        public string DepartmentId { get; set; }
        [ForeignKey("DepartmentId")]
        public Admin Department { get; set; }
        public string Block { get; set; }
    }
    public class LabData
    {
        [Column("lab_id")]
        [MaxLength(20)]
        public string LabId { get; set; }
        [Column("lab_name")]
        [MaxLength(100)]
        public string LabName { get; set; }
        [Column("lab_capacity")]
        public int? LabCapacity { get; set; }
        [Column("departmentid")]
        [MaxLength(20)]
        public string DepartmentId { get; set; }
        [ForeignKey("DepartmentId")]
        public Admin Department { get; set; }
        public string Block { get; set; }
    }
    public class PendingTimetableData
    {
        public int Id { get; set; }
        [Column("staff_id")]
        public string StaffId { get; set; }
        [Column("subject_id")]
        public string SubjectId { get; set; }
        [Column("lab_id")]
        public string LabId { get; set; }
        public string Staffname { get; set; }
        public string StaffDepartment { get; set; }
        public string SubjectShrtForm { get; set; }
        public int? Credit { get; set; }
        public string Subtype { get; set; }
        public string Department { get; set; }
        public string Year { get; set; }
        public string Sem { get; set; }
        public string LabDepartment { get; set; }
        public string Section { get; set; }
    }
    public class ClassTimetable
    {
        public int Id { get; set; }
        [Column("department_id")]
        public string DepartmentId { get; set; }
        public string Year { get; set; }
        public string Semester { get; set; }
        public string Section { get; set; }
        public string Day { get; set; }
        public int? Hour { get; set; }
        public string SubjectShortform { get; set; }
        [Column("subject_code")]
        public string SubjectCode { get; set; }
        public string SubjectName { get; set; }
        public string StaffName { get; set; }
        [Column("staff_code")]
        public string StaffCode { get; set; }
    }
    public class LabTimetable
    {
        public int Id { get; set; }
        [Column("lab_id")]
        public string LabId { get; set; }
        [Column("subject_code")]
        public string SubjectCode { get; set; }
        public string SubjectName { get; set; }
        public string StaffName { get; set; }
        [Column("staff_code")]
        public string StaffCode { get; set; }
        public string Department { get; set; }
        public string Year { get; set; }
        public string Semester { get; set; }
        public string Section { get; set; }
        public string Day { get; set; }
        public int? Hour { get; set; }
    }
    public class LibraryTimetable
    {
        public int Id { get; set; }
        public string StaffName { get; set; }
        public string Department { get; set; }
        public string Year { get; set; }
        public string Semester { get; set; }
        public string Section { get; set; }
        public string Day { get; set; }
        public int? Hour { get; set; }
    }
    public class AppDbContext : DbContext
    {
        public AppDbContext(DbContextOptions<AppDbContext> options) : base(options) { }
        public DbSet<Admin> Admins { get; set; }
        public DbSet<Login> Logins { get; set; }
        public DbSet<SubjectData> SubjectDatas { get; set; }
        public DbSet<StaffData> StaffDatas { get; set; }
        public DbSet<LabData> LabDatas { get; set; }
        public DbSet<PendingTimetableData> PendingTimetableDatas { get; set; }
        public DbSet<ClassTimetable> ClassTimetables { get; set; }
        public DbSet<LabTimetable> LabTimetables { get; set; }
        public DbSet<LibraryTimetable> LibraryTimetables { get; set; }
        protected override void OnModelCreating(ModelBuilder modelBuilder)
        {
            base.OnModelCreating(modelBuilder);

            // Configure primary keys where appropriate (based on your existing [Key] attributes)
            modelBuilder.Entity<Admin>()
                .HasKey(a => a.DepartmentId);

            modelBuilder.Entity<Login>()
                .HasKey(l => l.Username);

            modelBuilder.Entity<SubjectData>()
                .HasKey(s => s.SubjectCode);

            modelBuilder.Entity<StaffData>()
                .HasKey(s => s.StaffId);

            modelBuilder.Entity<LabData>()
                .HasKey(l => l.LabId);

            modelBuilder.Entity<PendingTimetableData>()
                .HasKey(p => p.Id);

            modelBuilder.Entity<ClassTimetable>()
                .HasKey(c => c.Id);

            modelBuilder.Entity<LabTimetable>()
                .HasKey(l => l.Id);

            modelBuilder.Entity<LibraryTimetable>()
                .HasKey(l => l.Id);

            // Configure relationships
            modelBuilder.Entity<Admin>()
                .HasOne(a => a.Login)
                .WithOne(l => l.Admin)
                .HasForeignKey<Login>(l => l.Username)
                .OnDelete(DeleteBehavior.Cascade);

            // Additional relationships based on your navigation properties can be configured here

            // For any entities intended as keyless (read-only), uncomment below and configure accordingly
            // modelBuilder.Entity<YourKeylessEntity>().HasNoKey();
        }

    }
}
