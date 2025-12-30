import React, { useState, useEffect } from 'react';
import { toast, ToastContainer } from 'react-toastify';
import 'react-toastify/dist/ReactToastify.css';
import '../styles/Table.css';


interface SubjectRecord {
  subjectId: string;
  subCode: string;
  subjectName: string;
  subjectType: string;
  credit: number;
  staff_assigned?: string;
  staff_department?: string; // Added to track staff department
  labId?: string; // ✅ Add this line
  lab_department?: string; // Added to track lab department
}


interface StaffMember {
  staffId: string;
  staffName: string;
}


const Table: React.FC = () => {
  const [year, setYear] = useState('');
  const [semester, setSemester] = useState('');
  const [semesterOptions, setSemesterOptions] = useState<string[]>([]);
  const [section, setSection] = useState('');
  const [department, setDepartment] = useState('');
  const [isAdmin, setIsAdmin] = useState(false);
  const [subjects, setSubjects] = useState<SubjectRecord[]>([]);
  const [staffList, setStaffList] = useState<StaffMember[]>([]);
  const [showSubjects, setShowSubjects] = useState(false);
  const [showStaffDeptPopup, setShowStaffDeptPopup] = useState(false);
  const [showLabDeptPopup, setShowLabDeptPopup] = useState(false);
  const [selectedSubject, setSelectedSubject] = useState<SubjectRecord | null>(null);
  const [allDepartments, setAllDepartments] = useState<string[]>([]);
  const [labs, setLabs] = useState<{ labId: string; labName: string; lab_department: string }[]>([]);
  const [showModal, setShowModal] = useState(false);


  useEffect(() => {
    fetch('https://localhost:7244/api/Login/departments')
      .then((res) => res.json())
      .then((data) => setAllDepartments(data))
      .catch(() => toast.error('Error loading departments'));
  }, []);


  useEffect(() => {
    const loggedUser = localStorage.getItem('loggedUser') || '';
    const isUserAdmin = loggedUser.toLowerCase() === 'admin';
    setIsAdmin(isUserAdmin);
    if (!isUserAdmin) {
      setDepartment(loggedUser);
    }
  }, []);


  const handleNext = async () => {
    if (!year || !semester || !section || !department) {
      toast.error('Please fill all fields!');
      return;
    }
    try {
      const subjectRes = await fetch(
        `https://localhost:7244/api/StaffSubject/subjects?year=${encodeURIComponent(year)}&sem=${encodeURIComponent(semester)}&departmentId=${encodeURIComponent(department)}`
      );
      const subjectData = await subjectRes.json();


      const staffRes = await fetch(
        `https://localhost:7244/api/StaffSubject/staff?departmentId=${encodeURIComponent(department)}`
      );
      const staffData = await staffRes.json();


      if (subjectData.some((s: any) => s.subjectType === 'Lab' || s.subjectType === 'Embedded')) {
        const labRes = await fetch(`https://localhost:7244/api/Lab/all?departmentId=${encodeURIComponent(department)}`);
        const labData = await labRes.json();

        setLabs(labData);
      }


      const subjectsWithStaff = subjectData.map((subject: any) => ({
        subjectId: subject.subjectId,
        subCode: subject.subCode,
        subjectName: subject.subjectName,
        subjectType: subject.subjectType,
        credit: subject.credit,
        staff_assigned: '',
        staff_department: department, // Default to current department
        labId: '', // Default empty lab id
       lab_department: department, // set default lab department to current department
 // Default empty lab department
      }));


      setSubjects(subjectsWithStaff);
      setStaffList(staffData);
      setShowSubjects(true);
      toast.success('Subjects and staff loaded successfully!');
    } catch (err) {
      toast.error('Error fetching subjects or staff');
    }
  };


  // Staff select handler adjusted for "Other Dept" to open department select popup (no submit)
  const handleStaffSelect = (subjectIndex: number, staffValue: string) => {
    if (staffValue === '__other__') {
      setSelectedSubject(subjects[subjectIndex]);
      setShowStaffDeptPopup(true);
      // Clear staff assignment and staff_department to be set by popup selection
      const updated = [...subjects];
      updated[subjectIndex].staff_assigned = `Other Dept: `; // placeholder to show in UI
      updated[subjectIndex].staff_department = '';
      setSubjects(updated);
    } else {
      const updated = [...subjects];
      updated[subjectIndex].staff_assigned = staffValue;
      // If selected from current department staff, update staff_department accordingly
      if (staffValue !== '') {
        updated[subjectIndex].staff_department = department;
      } else {
        updated[subjectIndex].staff_department = '';
      }
      setSubjects(updated);
    }
  };


  // Lab selection change handler for labId, also reset lab_department if lab changes
  const handleLabChange = (subjectIndex: number, labId: string) => {
    const updated = [...subjects];
    updated[subjectIndex].labId = labId;
    // Reset lab_department to lab's actual department or empty
    const selectedLab = labs.find((lab) => lab.labId === labId);
    updated[subjectIndex].lab_department = selectedLab ? selectedLab.lab_department : '';
    setSubjects(updated);
  };


  // Lab department change popup select (no submit, immediate update)
  const handleLabDeptChange = (dept: string) => {
    if (!selectedSubject) return;
    const updated = [...subjects];
    const index = updated.findIndex((sub) => sub.subCode === selectedSubject.subCode);
    if (index === -1) return;


    updated[index].lab_department = dept;


    // If lab department changed to other department, clear labId per your requirement
    const labDeptSameAsDepartment = dept === department;
    if (!labDeptSameAsDepartment) {
      updated[index].labId = '';
    }


    setSubjects(updated);
  };


  // Staff department change popup select (no submit, immediate update)
  const handleStaffDeptChange = (dept: string) => {
    if (!selectedSubject) return;
    const updated = [...subjects];
    const index = updated.findIndex((sub) => sub.subCode === selectedSubject.subCode);
    if (index === -1) return;


    updated[index].staff_department = dept;


    // If staff department changed to other department, clear staff_assigned per your requirement
    const staffDeptSameAsDepartment = dept === department;
    if (!staffDeptSameAsDepartment) {
      updated[index].staff_assigned = `Other Dept: ${dept}`;
    }


    setSubjects(updated);
  };


  const handleYearChange = (e: React.ChangeEvent<HTMLSelectElement>) => {
    const selectedYear = e.target.value;
    setYear(selectedYear);
    setSemester('');


    switch (selectedYear) {
      case 'First Year':
        setSemesterOptions(['I', 'II']);
        break;
      case 'Second Year':
        setSemesterOptions(['III', 'IV']);
        break;
      case 'Third Year':
        setSemesterOptions(['V', 'VI']);
        break;
      case 'Fourth Year':
        setSemesterOptions(['VII', 'VIII']);
        break;
      default:
        setSemesterOptions([]);
        break;
    }
  };
const anyOtherDeptAssigned = subjects.some(
  sub =>
    (sub.staff_department && sub.staff_department !== department) ||
    ((sub.subjectType === 'Lab' || sub.subjectType === 'Embedded') && sub.lab_department && sub.lab_department !== department)
);

  return (
    <div className="table-wrapper">
      <ToastContainer position="top-right" autoClose={3000} hideProgressBar />


      <div className="form-grid">
        <div className="form-row">
          <label>Year</label>
          <select value={year} onChange={handleYearChange}>
            <option value="">Select</option>
            <option value="First Year">First Year</option>
            <option value="Second Year">Second Year</option>
            <option value="Third Year">Third Year</option>
            <option value="Fourth Year">Fourth Year</option>
          </select>
        </div>


        <div className="form-row">
          <label>Semester</label>
          <select value={semester} onChange={(e) => setSemester(e.target.value)}>
            <option value="">Select</option>
            {semesterOptions.map((sem) => (
              <option key={sem} value={sem}>
                {sem}
              </option>
            ))}
          </select>
        </div>


        <div className="form-row">
          <label>Section</label>
          <select value={section} onChange={(e) => setSection(e.target.value)}>
            <option value="">Select</option>
            <option value="A">A</option>
            <option value="B">B</option>
            <option value="C">C</option>
          </select>
        </div>


        <div className="form-row">
          <label>Department</label>
          <input
            type="text"
            value={department}
            onChange={(e) => setDepartment(e.target.value)}
            disabled={!isAdmin}
            placeholder={isAdmin ? 'Enter department' : ''}
          />
        </div>
      </div>


      <div className="submit-row">
        <button className="next-btn" onClick={handleNext}>
          Next
        </button>
      </div>


      {showSubjects && (
        <div className="subject-list">
          <h3>Subjects Found: {subjects.length}</h3>
          <table>
            <thead>
              <tr>
                <th>Subject ID</th>
                <th>Code</th>
                <th>Name</th>
                <th>Type</th>
                <th>Credit</th>
                <th>Staff</th>
             <th>Lab</th>

               
              </tr>
            </thead>
            <tbody>
              {subjects.map((subj, idx) => (
                <tr key={idx}>
                  <td>{subj.subjectId}</td>
                  <td>{subj.subCode}</td>
                  <td>{subj.subjectName}</td>
                  <td>{subj.subjectType}</td>
                  <td>{subj.credit}</td>
                  <td>
<td>
 <select
  value={
    subj.staff_department && subj.staff_department !== department
      ? "From Other Department"
      : subj.staff_assigned || ""
  }
  onChange={e => {
    if (e.target.value === "From Other Department" || e.target.value === "__other__") {
      setSelectedSubject(subj);
      setShowStaffDeptPopup(true);
    } else {
      handleStaffSelect(idx, e.target.value);
    }
  }}
>
  <option value="">Select</option>
  {staffList.map((s) => (
    <option key={s.staffId} value={`${s.staffName} (${s.staffId})`}>
      {s.staffName} ({s.staffId})
    </option>
  ))}
  <option value="From Other Department">From Other Department</option>
</select>


</td>


</td>
<td>
  {(subj.subjectType === 'Lab' || subj.subjectType === 'Embedded') ? (
    subj.lab_department && subj.lab_department !== department ? (
      <select
        value="From Other Department"
        onChange={e => {
          if (e.target.value === "From Other Department" || e.target.value === "__other__") {
            setSelectedSubject(subj);
            setShowLabDeptPopup(true);
            const updated = [...subjects];
            const index = updated.findIndex(sub => sub.subCode === subj.subCode);
            if (index !== -1) {
              updated[index].labId = "";
              updated[index].lab_department = "";
              setSubjects(updated);
            }
          } else {
            handleLabChange(idx, e.target.value);
          }
        }}
      >
        <option value="From Other Department">From Other Department</option>
        <option value="">Select Lab</option>
        {labs
         .filter(lab => lab.lab_department === department)
          .map(lab => (
            <option key={lab.labId} value={lab.labId}>
              {lab.labId}
            </option>
          ))}
      </select>
      
    ) : (
      <select
        value={subj.labId || ""}
        onChange={e => {
          if (e.target.value === "From Other Department" || e.target.value === "__other__") {
            setSelectedSubject(subj);
            setShowLabDeptPopup(true);

            const updated = [...subjects];
            updated[idx].labId = "";
            updated[idx].lab_department = "";
            setSubjects(updated);
          } else {
            handleLabChange(idx, e.target.value);
          }
        }}
      >
        <option value="">Select Lab</option>
        {labs
          //.filter(lab => lab.lab_department === department)
          .map(lab => (
            <option key={lab.labId} value={lab.labId}>
              {lab.labId}
            </option>
          ))}
        <option value="From Other Department">From Other Department</option>
      </select>
    )
  ) : (
    '---'
  )}
</td>



                </tr>
              ))}
            </tbody>
          </table>
          <div className="submit-row">
            {anyOtherDeptAssigned ? (
           <button
  className="wait-btn"
  onClick={async () => {
    try {
      // Optional: you can remove or adjust validation if needed, here commented out
      // for (const sub of subjects) {
      //   if (
      //     !sub.staff_department ||
      //     (sub.subjectType === 'Lab' &&
      //      !sub.lab_department &&
      //      sub.labId !== '')
      //   ) {
      //     toast.error('Please assign all staff and lab departments properly before saving.');
      //     return;
      //   }
      // }

      // Map subjects to DTO-compliant format
      const payloadSubjects = subjects.map((sub) => {
        const isStaffOtherDept = sub.staff_department !== department;
        const isLabOtherDept =
          (sub.subjectType === 'Lab' || sub.subjectType === 'Embedded') && sub.lab_department !== department;

        return {
          StaffId: isStaffOtherDept ? "From Other Department" : "", // assign actual StaffId if available
          StaffName: isStaffOtherDept ? "From Other Department" : (sub.staff_assigned || ""),
          SubjectCode: sub.subCode || "",
          SubjectShortForm:  sub.subjectName, // add value if you have it
          Credit: sub.credit,
          SubjectType: sub.subjectType || "",
        LabId: isLabOtherDept && (!sub.labId || sub.labId.trim() === '') ? "From Other Department" : (sub.labId || '')
,
          StaffDepartment: sub.staff_department || department,
          LabDepartment: sub.lab_department || ((sub.subjectType === 'Embedded' || sub.subjectType === 'Lab') ? department : "")
        };
      });

      const payload = {
        Department: department,
        Year: year,
        Semester: semester,
        Section: section,
        Subjects: payloadSubjects
      };

      console.log(payload);

      const res = await fetch("https://localhost:7244/api/CrossDepartmentAssignments/save", {
        method: "POST",
        headers: { "Content-Type": "application/json" },
        body: JSON.stringify(payload),
      });

      if (res.ok) {
        toast.success("Saved! Waiting for approval request submitted.");
      } else {
        toast.error("Failed to save approval request.");
      }
    } catch (err) {
      toast.error("Failed to save approval request.");
    }
  }}
>
  Save and Wait for Approval
</button>

            ) : (
              <button
                className="generate-btn"
                disabled={anyOtherDeptAssigned}
                onClick={async () => {
                  try {
                    if (subjects.some((s) => !s.staff_assigned || s.staff_assigned.trim() === '')) {
                      toast.error('Please assign all staff before generating timetable.');
                      return;
                    }
                    const payload = {
                      department,
                      year,
                      semester,
                      section,
                      subjects: subjects.map((sub) => ({
                        subjectCode: sub.subCode,
                        subjectName: sub.subjectName,
                        subjectType: sub.subjectType,
                        credit: sub.credit,
                        staffAssigned: sub.staff_assigned ?? '',
                        labId: sub.labId ?? '',
                        
                      })),
                    };

console.log('Generating timetable with payload:', payload);
                    const payloadString = JSON.stringify(payload, null, 2);


                // const res = await fetch('https://localhost:7244/api/Timetable/generateCrossDepartmentTimetableBacktracking', {
                     const res = await fetch('https://localhost:7244/api/EnhancedTimetable/generateCrossDepartmentTimetableBacktracking', {
                      method: 'POST',
                      headers: { 'Content-Type': 'application/json' },
                      body: payloadString,
                    });


                    const result = await res.json();


                    toast.success(result.message);
                  } catch (err) {
                    toast.error('Failed to generate timetable!');
                  }
                }}
              >
                Generate
              </button>
            )}
          </div>
        </div>
      )}
{showLabDeptPopup && selectedSubject && (
  <div className="modal-backdrop" onClick={() => setShowLabDeptPopup(false)}>
    <div className="modal-content" onClick={e => e.stopPropagation()}>
      <h3>Select Lab Department for {selectedSubject.subCode}</h3>
      <select
        value={selectedSubject.lab_department || ""}
        onChange={e => {
          handleLabDeptChange(e.target.value);
          setShowLabDeptPopup(false);
        }}
      >
        <option value="">Select Department</option>
        {[department, ...allDepartments.filter(d => d !== department)].map(dept => (
          <option key={dept} value={dept}>{dept}</option>
        ))}
      </select>
    </div>
  </div>
)}

    {showStaffDeptPopup && selectedSubject && (
  /* modal content */



        <div className="modal-backdrop" onClick={() => setShowStaffDeptPopup(false)}>
          <div className="modal-content" onClick={(e) => e.stopPropagation()}>
            <h3>Select Staff Department</h3>
            <p>
              <strong>From Department:</strong> {department}
            </p>
            <p>
              <strong>Subject Code:</strong> {selectedSubject.subCode}
            </p>
            <p>
              <strong>Subject Name:</strong> {selectedSubject.subjectName}
            </p>
            <select
              value={selectedSubject.staff_department || ''}
              onChange={(e) => {
                handleStaffDeptChange(e.target.value);
                setShowStaffDeptPopup(false); // Close modal after selection
              }}
            >
              <option value="">Select Department</option>
              {[department, ...allDepartments.filter((d) => d !== department)].map((dept) => (
                <option key={dept} value={dept}>
                  {dept}
                </option>
              ))}
            </select>
          </div>
        </div>
      )}


      {/* Lab department dropdown handled inline in table, no separate popup needed */}


      {/* Staff assignment other department popup no longer has submit button per requirement */}
      {showModal && selectedSubject && (
        <div className="modal-backdrop" onClick={() => setShowModal(false)}>
          <div className="modal-content" onClick={(e) => e.stopPropagation()}>
            <h3>Assign from Other Department</h3>
            <p>
              <strong>From Department:</strong> {department}
            </p>
            <p>
              <strong>Subject Code:</strong> {selectedSubject.subCode}
            </p>
            <p>
              <strong>Subject Name:</strong> {selectedSubject.subjectName}
            </p>


            <label>
              <strong>To Department:</strong>
              <select
                value={selectedSubject.staff_department || ''}
                onChange={(e) => {
                  handleStaffDeptChange(e.target.value);
                }}
              >
                <option value="">Select Department</option>
                {allDepartments
                  .filter((dept) => dept !== department)
                  .map((dept) => (
                    <option key={dept} value={dept}>
                      {dept}
                    </option>
                  ))}
                <option value={department}>{department}</option>
              </select>
            </label>
          </div>
        </div>
      )}
    </div>
  );
};


export default Table;