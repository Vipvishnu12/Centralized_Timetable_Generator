import React, { useEffect, useState } from 'react';
import '../styles/subject.css';

interface SubjectRecord {
  sub_Code: string;
  subject_Name: string;
  subject_Type: string;
  credit: number;
  year: string;
  sem: string;
  department: string;
}

const ViewSubject: React.FC = () => {
  const [year, setYear] = useState('');
  const [semester, setSemester] = useState('');
  const [department, setDepartment] = useState('');
  const [isAdmin, setIsAdmin] = useState(false);
  const [subjectList, setSubjectList] = useState<SubjectRecord[]>([]);
  const [editIndex, setEditIndex] = useState<number | null>(null);
  const [editedSubject, setEditedSubject] = useState<SubjectRecord | null>(null);

  const years = ['First Year', 'Second Year', 'Third Year', 'Fourth Year'];
  const departments = ['CSE', 'ECE', 'EEE', 'MECH', 'CIVIL'];

  const getSemestersByYear = (selectedYear: string): string[] => {
    switch (selectedYear) {
      case 'First Year': return ['I', 'II'];
      case 'Second Year': return ['III', 'IV'];
      case 'Third Year': return ['V', 'VI'];
      case 'Fourth Year': return ['VII', 'VIII'];
      default: return [];
    }
  };

  useEffect(() => {
    const loggedUser = localStorage.getItem('loggedUser') || '';
    const isUserAdmin = loggedUser.toLowerCase() === 'admin';

    setIsAdmin(isUserAdmin);
    if (!isUserAdmin) setDepartment(loggedUser.toUpperCase());
  }, []);

  const handleYearChange = (e: React.ChangeEvent<HTMLSelectElement>) => {
    const selectedYear = e.target.value;
    setYear(selectedYear);
    setSemester('');
  };

  const handleFetch = async () => {
    try {
      const response = await fetch(
        `https://localhost:7244/api/SubjectData/view/${encodeURIComponent(year)}/${encodeURIComponent(semester)}/${encodeURIComponent(department)}`
      );
      const text = await response.text();
      const data = text ? JSON.parse(text) : [];
      setSubjectList(data);
    } catch (error) {
      console.error('Failed to fetch subjects:', error);
    }
  };

  useEffect(() => {
    if (year && semester && department) {
      handleFetch();
    }
  }, [year, semester, department]);

  const handleEdit = (index: number) => {
    setEditIndex(index);
    setEditedSubject({ ...subjectList[index] });
  };

  const handleChange = (field: keyof SubjectRecord, value: string | number) => {
    if (!editedSubject) return;
    setEditedSubject({ ...editedSubject, [field]: value });
  };

  const handleSave = async () => {
    if (!editedSubject) return;

    try {
      const response = await fetch(`https://localhost:7244/api/SubjectData/update`, {
        method: 'PUT',
        headers: { 'Content-Type': 'application/json' },
        body: JSON.stringify(editedSubject),
      });

      const result = await response.json();
      alert(result.message || '✅ Subject updated successfully');

      const updatedList = [...subjectList];
      if (editIndex !== null) updatedList[editIndex] = editedSubject;
      setSubjectList(updatedList);

      setEditIndex(null);
      setEditedSubject(null);
    } catch (error) {
      alert('❌ Failed to update subject');
      console.error(error);
    }
  };

  const handleCancel = () => {
    setEditIndex(null);
    setEditedSubject(null);
  };

  return (
    <div className="subject-wrapper">
      <h2 className="grid-title">View Subjects</h2>

      <div className="subject-grid-row">
        <div className="subject-grid-item">
          <label className="subject-label">Year</label>
          <select value={year} onChange={handleYearChange} className="dropdown">
            <option value="">Select</option>
            {years.map((yr) => <option key={yr} value={yr}>{yr}</option>)}
          </select>
        </div>

        <div className="subject-grid-item">
          <label className="subject-label">Semester</label>
          <select value={semester} onChange={(e) => setSemester(e.target.value)} className="dropdown">
            <option value="">Select</option>
            {getSemestersByYear(year).map((sem) => <option key={sem} value={sem}>{sem}</option>)}
          </select>
        </div>

        <div className="subject-grid-item">
          <label className="subject-label">Department</label>
          <select
            value={department}
            onChange={(e) => setDepartment(e.target.value)}
            className="dropdown"
            disabled={!isAdmin}
          >
            <option value="">Select</option>
            {departments.map((dept) => <option key={dept} value={dept}>{dept}</option>)}
          </select>
        </div>
      </div>

      {subjectList.length > 0 && (
        <div className="subject-list">
          <h3>Subjects Found: {subjectList.length}</h3>
          <table>
            <thead>
              <tr>
                <th>Code</th>
                <th>Name</th>
                <th>Type</th>
                <th>Credit</th>
                <th>Year</th>
                <th>Semester</th>
                <th>Department</th>
                <th>Action</th>
              </tr>
            </thead>
            <tbody>
              {subjectList.map((subj, idx) => {
                const isEditing = idx === editIndex;
                const record = isEditing ? editedSubject : subj;

                return (
                  <tr key={idx}>
                    <td>{record?.sub_Code}</td>
                    <td>
                      <input
                        type="text"
                        value={record?.subject_Name || ''}
                        onChange={(e) => handleChange('subject_Name', e.target.value)}
                        readOnly={!isEditing}
                      />
                    </td>
                    <td>
                      <input
                        type="text"
                        value={record?.subject_Type || ''}
                        onChange={(e) => handleChange('subject_Type', e.target.value)}
                        readOnly={!isEditing}
                      />
                    </td>
                    <td>
                      <input
                        type="number"
                        value={record?.credit || ''}
                        onChange={(e) => handleChange('credit', parseInt(e.target.value))}
                        readOnly={!isEditing}
                      />
                    </td>
                    <td>{record?.year}</td>
                    <td>{record?.sem}</td>
                    <td>{record?.department}</td>
                    <td className="action-buttons">
                        {isEditing ? (
                          <>
                            <button onClick={handleSave} className="save-button">Save</button>
                            <button onClick={handleCancel} className="cancel-button">Cancel</button>
                          </>
                        ) : (
                          <button onClick={() => handleEdit(idx)} className="edit-button">Edit</button>
                        )}
                      </td>
                  </tr>
                );
              })}
            </tbody>
          </table>
        </div>
      )}
    </div>
  );
};

export default ViewSubject;
