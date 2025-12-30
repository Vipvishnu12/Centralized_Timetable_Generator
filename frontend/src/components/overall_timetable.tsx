import React, { useEffect, useState } from 'react';
import '../styles/overall_timetable.css';
import { toast, ToastContainer } from 'react-toastify';
import 'react-toastify/dist/ReactToastify.css';

interface PeriodRecord {
  staffId: string;
  staffName: string;
  subjectCode: string;
  subjectName: string;
  count: number;
  department: string;
}

const OverallStaff: React.FC = () => {
  const [periods, setPeriods] = useState<PeriodRecord[]>([]);
  const [loading, setLoading] = useState(true);
  const [selectedDepartment, setSelectedDepartment] = useState('');
  const [allDepartments, setAllDepartments] = useState<string[]>([]);

  const loggedUser = localStorage.getItem('loggedUser')?.toLowerCase() || '';
  const isAdmin = loggedUser === 'admin';

  useEffect(() => {
    const fetchData = async () => {
      try {
        const res = await fetch('https://localhost:7244/api/CrossDepartmentAssignments/periods');
        if (!res.ok) throw new Error('Server error');
        const data: PeriodRecord[] = await res.json();

        const departments = [...new Set(data.map((d) => d.department))];
        setAllDepartments(departments);

        const defaultDept = isAdmin ? '' : loggedUser.toUpperCase();
        setSelectedDepartment(defaultDept);

        const filtered = isAdmin
          ? data
          : data.filter((d) => d.department.toLowerCase() === loggedUser);

        setPeriods(filtered);
        if (filtered.length === 0) toast.info('ℹ️ No data found.');
      } catch (err) {
        console.error(err);
        toast.error('❌ Failed to load data.');
      } finally {
        setLoading(false);
      }
    };

    fetchData();
  }, [loggedUser, isAdmin]);

  useEffect(() => {
    const fetchFiltered = async () => {
      if (!selectedDepartment) return;

      try {
        const res = await fetch('https://localhost:7244/api/CrossDepartmentAssignments/periods');
        const data: PeriodRecord[] = await res.json();
        const filtered = data.filter((d) => d.department === selectedDepartment);
        setPeriods(filtered);
      } catch {
        setPeriods([]);
      }
    };

    if (isAdmin || selectedDepartment) {
      fetchFiltered();
    }
  }, [selectedDepartment]);

  const grouped = periods.reduce((acc: Record<string, PeriodRecord[]>, record) => {
    if (!acc[record.staffId]) acc[record.staffId] = [];
    acc[record.staffId].push(record);
    return acc;
  }, {});

  return (
    <div className="overall-staff-container">
      <h2>📊 Overall Staff Subject Period Counts</h2>

      <div
        style={{
          marginBottom: '20px',
          marginLeft: '20px',
          display: 'flex',
          flexDirection: 'column',
          gap: '10px',
        }}
      >
        <label>Department:</label>
        <select
          value={selectedDepartment}
          onChange={(e) => setSelectedDepartment(e.target.value)}
          disabled={!isAdmin}
          style={{
            padding: '8px',
            width: '250px',
            backgroundColor: isAdmin ? 'white' : '#f0f0f0',
          }}
        >
          <option value="">-- Select Department --</option>
          {allDepartments.map((dept, idx) => (
            <option key={idx} value={dept}>
              {dept}
            </option>
          ))}
       </select>
      </div>

      {loading ? (
        <p>⏳ Loading data...</p>
      ) : Object.keys(grouped).length === 0 ? (
        <p>❌ No records found.</p>
      ) : (
        <div className="table-wrapper">
          <table className="staff-table">
            <thead>
              <tr>
                <th>S.No</th>
                <th>Department</th>
                <th>Staff ID</th>
                <th>Staff Name</th>
                <th>Subjects & Periods</th>
              </tr>
            </thead>
            <tbody>
              {Object.entries(grouped).map(([staffId, records], index) => (
                <tr key={staffId}>
                  <td>{index + 1}</td>
                  <td>{records[0].department}</td>
                  <td>{staffId}</td>
                  <td>{records[0].staffName}</td>
                  <td>
                    <ul style={{ paddingLeft: '1rem', margin: 0 }}>
                      {records.map((r, i) => (
                        <li key={i}>
                          {r.subjectName} ({r.subjectCode}) — <strong>{r.count}</strong> periods
                        </li>
                      ))}
                    </ul>
                  </td>
                </tr>
              ))}
            </tbody>
          </table>
        </div>
      )}

      <ToastContainer position="top-right" autoClose={3000} hideProgressBar />
    </div>
  );
};

export default OverallStaff;
