import React, { useEffect, useState } from 'react';
import '../styles/ViewLab.css';

interface Lab {
  labId: string;
  labName: string;
  department: string;
  systems: number;
}

const ViewLab: React.FC = () => {
  const [labList, setLabList] = useState<Lab[]>([]);
  const [editLab, setEditLab] = useState<Lab | null>(null);
  const [error, setError] = useState('');

  const loggedUser = localStorage.getItem('loggedUser')?.toLowerCase() || '';
  const isAdmin = loggedUser === 'admin';

  const fetchLabs = async () => {
    try {
      const response = await fetch('https://localhost:7244/api/Lab/all1');
      if (!response.ok) throw new Error('Failed to fetch lab data');

      const data: Lab[] = await response.json();

      const filtered = isAdmin
        ? data
        : data.filter((lab) => lab.department.toLowerCase() === loggedUser);

      setLabList(filtered);
    } catch (err: any) {
      console.error(err);
      setError(err.message || 'Something went wrong');
    }
  };

  useEffect(() => {
    fetchLabs();
  }, []);

  const handleEditClick = (lab: Lab) => {
    setEditLab({ ...lab });
  };

  const handleEditChange = (e: React.ChangeEvent<HTMLInputElement>) => {
    if (editLab) {
      setEditLab({ ...editLab, [e.target.name]: e.target.value });
    }
  };

  const handleUpdate = async () => {
    if (!editLab) return;

    try {
      const response = await fetch('https://localhost:7244/api/Lab/update', {
        method: 'PUT',
        headers: { 'Content-Type': 'application/json' },
        body: JSON.stringify({
          labId: editLab.labId,
          labName: editLab.labName,
          department: editLab.department,
          systems: Number(editLab.systems),
          block: "", // default empty block
        }),
      });

      if (!response.ok) throw new Error('Update failed');

      setEditLab(null);
      fetchLabs();
    } catch (err: any) {
      console.error(err);
      alert(err.message || 'Error updating lab');
    }
  };

  return (
    <div className="viewlab-wrapper">
      <h2 className="grid-title">Lab Details</h2>

      <div className="lab-list">
        {error && <p className="error">❌ {error}</p>}

        {labList.length > 0 ? (
          <table>
            <thead>
              <tr>
                <th>Lab ID</th>
                <th>Lab Name</th>
                <th>Department</th>
                <th>No. of Systems</th>
                <th>Actions</th>
              </tr>
            </thead>
            <tbody>
              {labList.map((lab) => (
                <tr key={lab.labId}>
                  <td>{lab.labId}</td>
                  <td>
                    {editLab?.labId === lab.labId ? (
                      <input
                        name="labName"
                        value={editLab.labName}
                        onChange={handleEditChange}
                      />
                    ) : (
                      lab.labName
                    )}
                  </td>
                  <td>
                    {editLab?.labId === lab.labId ? (
                      <input
                        name="department"
                        value={editLab.department}
                        onChange={handleEditChange}
                      />
                    ) : (
                      lab.department
                    )}
                  </td>
                  <td>
                    {editLab?.labId === lab.labId ? (
                      <input
                        name="systems"
                        type="number"
                        value={editLab.systems}
                        onChange={handleEditChange}
                      />
                    ) : (
                      lab.systems
                    )}
                  </td>
               
                   <td className="action-buttons">
                        {editLab?.labId === lab.labId ? (
                          <>
                            <button onClick={handleUpdate} className="save-button">Save</button>
                            <button onClick={() => setEditLab(null)} className="cancel-button">Cancel</button>
                          </>
                        ) : (
                          <button onClick={() => handleEditClick(lab)} className="edit-button">Edit</button>
                        )}
                      </td>
                </tr>
              ))}
            </tbody>
          </table>
        ) : (
          !error && <p>📭 No labs available.</p>
        )}
      </div>
    </div>
  );
};

export default ViewLab;
