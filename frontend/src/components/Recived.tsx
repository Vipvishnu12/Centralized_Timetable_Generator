import React, { useEffect, useState } from 'react';
import '../styles/Recived.css';

interface SubjectRecord {
  id: number;
  fromDepartment: string;
  toDepartment: string;
  subCode: string;
  subjectName: string;
  year: string;
  semester: string;
  section: string;
  assignedStaff?: string;
}

interface StaffMember {
  staffId: string;
  staffName: string;
}

const Received: React.FC = () => {
  const [subjects, setSubjects] = useState<SubjectRecord[]>([]);
  const [staffList, setStaffList] = useState<StaffMember[]>([]);
  const [loading, setLoading] = useState(true);
  const loggedUser = localStorage.getItem('loggedUser') || '';

  const fetchReceived = async () => {
    try {
      const res = await fetch(`https://localhost:7244/api/CrossDepartmentAssignments/grouped1`);
      const data = await res.json();
      const filtered = data.filter((item: SubjectRecord) => item.toDepartment === loggedUser);
      setSubjects(filtered);
    } catch {
      setSubjects([]);
    } finally {
      setLoading(false);
    }
  };

  const fetchStaff = async () => {
    try {
      const res = await fetch(
        `https://localhost:7244/api/StaffSubject/staff?departmentId=${encodeURIComponent(loggedUser)}`
      );
      const data = await res.json();
      setStaffList(data);
    } catch {
      setStaffList([]);
    }
  };

  useEffect(() => {
    fetchReceived();
    fetchStaff();
  }, [loggedUser]);

  const handleStaffSelect = (subjectIndex: number, staffValue: string) => {
    const updated = [...subjects];
    updated[subjectIndex].assignedStaff = staffValue;
    setSubjects(updated);
  };

  const handleSubmit = async () => {
    try {
      const payload = subjects.map((s) => ({
        subCode: s.subCode,
        subjectName: s.subjectName,
        year: s.year,
        semester: s.semester,
        section: s.section,
        assignedStaff: s.assignedStaff,
      }));

      const res = await fetch('https://localhost:7244/api/StaffRequestData/updateAssignedStaff', {
        method: 'POST',
        headers: { 'Content-Type': 'application/json' },
        body: JSON.stringify(payload),
      });

      const result = await res.json();
      alert(result.message || 'Assignments updated successfully!');

      // 🔄 Refresh the data after successful update
      setLoading(true);
      await fetchReceived();
    } catch (err) {
      alert('Failed to update staff assignments');
      console.error(err);
    }
  };

  return (
    <div className="table-wrapper">
      <h2 style={{ textAlign: 'center', marginBottom: 24 }}>Received Staff Assignments</h2>
      <div className="subject-list">
        <table>
          <thead>
            <tr>
              <th>Code</th>
              <th>Name</th>
              <th>From Dept</th>
              <th>Year</th>
              <th>Semester</th>
              <th>Section</th>
              <th>Staff Assigned</th>
            </tr>
          </thead>
          <tbody>
            {loading ? (
              <tr>
                <td colSpan={7} style={{ textAlign: 'center', color: '#888' }}>Loading...</td>
              </tr>
            ) : subjects.length === 0 ? (
              <tr>
                <td colSpan={7} style={{ textAlign: 'center', color: '#888' }}>No received assignments</td>
              </tr>
            ) : (
              subjects.map((subj, idx) => (
                <tr key={subj.id}>
                  <td>{subj.subCode}</td>
                  <td>{subj.subjectName}</td>
                  <td>{subj.fromDepartment}</td>
                  <td>{subj.year}</td>
                  <td>{subj.semester}</td>
                  <td>{subj.section}</td>
                  <td>
                    <select
                      value={subj.assignedStaff || ''}
                      onChange={(e) => handleStaffSelect(idx, e.target.value)}
                    >
                      <option value="">Select</option>
                      {staffList.map((s) => (
                        <option key={s.staffId} value={`${s.staffName} (${s.staffId})`}>
                          {s.staffName} ({s.staffId})
                        </option>
                      ))}
                    </select>
                  </td>
                </tr>
              ))
            )}
          </tbody>
        </table>
      </div>

      {subjects.length > 0 && (
        <div style={{ textAlign: 'center', marginTop: 24 }}>
          <button onClick={handleSubmit} style={{ padding: '10px 20px' }}>
            Submit Assignments
          </button>
        </div>
      )}
    </div>
  );
};

export default Received;
