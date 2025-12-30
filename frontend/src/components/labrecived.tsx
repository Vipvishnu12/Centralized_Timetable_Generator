import React, { useEffect, useState } from 'react';
import { toast, ToastContainer } from 'react-toastify';
import 'react-toastify/dist/ReactToastify.css';
import '../styles/Recived.css';

interface LabRequest {
  subjectId: string;
  labId: string;
  subjectShrt: string;
  credit: number;
  subtype: string;
  department: string;
  year: string;
  sem: string;
  labDepartment: string;
  section: string;
  assignedLab?: string;
}

interface LabFacility {
  labId: string;
  labName: string;
  department: string;
  systems: number;
}

const LabReceived: React.FC = () => {
  const [labs, setLabs] = useState<LabRequest[]>([]);
  const [labFacilities, setLabFacilities] = useState<LabFacility[]>([]);
  const [loading, setLoading] = useState(true);
  const loggedUser = localStorage.getItem('loggedUser') || '';

  const fetchReceivedLabs = async () => {
    try {
      const res = await fetch(`https://localhost:7244/api/Lab/received`);
      if (!res.ok) throw new Error('Failed to fetch lab data');

      const data: LabRequest[] = await res.json();

      const filtered = data.filter(
        (item) => item.labDepartment.toLowerCase() === loggedUser.toLowerCase()
      );

      setLabs(filtered);
    } catch (error) {
      console.error('Fetch error:', error);
      toast.error('❌ Failed to load lab requests');
      setLabs([]);
    } finally {
      setLoading(false);
    }
  };

  const fetchLabFacilities = async () => {
    try {
      const res = await fetch(
        `https://localhost:7244/api/Lab/all?departmentId=${encodeURIComponent(loggedUser)}`
      );
      if (!res.ok) throw new Error('Failed to fetch lab facilities');

      const data: LabFacility[] = await res.json();
      setLabFacilities(data);
    } catch (error) {
      console.error('Fetch facilities error:', error);
      toast.error('❌ Failed to load lab facilities');
    }
  };

  useEffect(() => {
    fetchReceivedLabs();
    fetchLabFacilities();
  }, [loggedUser]);

  const handleLabSelect = (index: number, value: string) => {
    const updated = [...labs];
    updated[index].assignedLab = value;
    setLabs(updated);
  };
const handleSubmit = async () => {
  try {
    const unassigned = labs.filter((l) => !l.assignedLab);
    if (unassigned.length > 0) {
      toast.warn('Please assign labs to all requests');
      return;
    }

    // ✅ Match only the required fields for backend
    const payload = labs.map((l) => ({
      SubjectId: l.subjectId,
      AssignedLab: l.assignedLab,
      Section: l.section,
      Year: l.year,
    Semester: l.sem,
    LabDepartment: loggedUser,
    }));
console.log(payload);
    const res = await fetch(`https://localhost:7244/api/Lab/updateAssignedLabs`, {
      method: 'POST',
      headers: { 'Content-Type': 'application/json' },
      body: JSON.stringify(payload),
    });

    if (!res.ok) throw new Error('Failed to submit lab assignments');

    const result = await res.json();
    toast.success(result.message || '✅ Lab assignments submitted successfully!');
    await fetchReceivedLabs(); // refresh updated labs
  } catch (err) {
    console.error(err);
    toast.error('❌ Submission failed');
  }
};

  return (
    <div className="table-wrapper">
      <ToastContainer position="top-right" autoClose={3000} />
      <h2 style={{ textAlign: 'center', marginBottom: 24 }}>Cross Department Lab Requests</h2>
      <div className="subject-list">
        <table>
          <thead>
            <tr>
              <th>Subject Code</th>
              <th>Subject Name</th>
              <th>Credit</th>
              <th>Type</th>
              <th>From Dept</th>
              <th>Year</th>
              <th>Semester</th>
              <th>Section</th>
              <th>Requested Lab Dept</th>
              <th>Lab ID</th>
              <th>Assign Lab</th>
            </tr>
          </thead>
          <tbody>
            {loading ? (
              <tr>
                <td colSpan={11} style={{ textAlign: 'center', color: '#888' }}>Loading...</td>
              </tr>
            ) : labs.length === 0 ? (
              <tr>
                <td colSpan={11} style={{ textAlign: 'center', color: '#888' }}>No lab requests found</td>
              </tr>
            ) : (
              labs.map((lab, index) => (
                <tr key={index}>
                  <td>{lab.subjectId}</td>
                  <td>{lab.subjectShrt}</td>
                  <td>{lab.credit}</td>
                  <td>{lab.subtype}</td>
                  <td>{lab.department}</td>
                  <td>{lab.year}</td>
                  <td>{lab.sem}</td>
                  <td>{lab.section}</td>
                  <td>{lab.labDepartment}</td>
                  <td>{lab.labId}</td>
                  <td>
                    <select
                      value={lab.assignedLab || ''}
                      onChange={(e) => handleLabSelect(index, e.target.value)}
                    >
                      <option value="">Select</option>
                      {labFacilities.map((f) => (
                        <option key={f.labId} value={f.labId}>
                          {f.labId}
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

      {labs.length > 0 && (
        <div style={{ textAlign: 'center', marginTop: 24 }}>
          <button onClick={handleSubmit} className="submit-button">
            Submit Lab Assignments
          </button>
        </div>
      )}
    </div>
  );
};

export default LabReceived;
