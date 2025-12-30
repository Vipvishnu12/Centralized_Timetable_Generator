import React, { useState, useEffect } from 'react';
import '../styles/subject.css';
import { toast, ToastContainer } from 'react-toastify';
import 'react-toastify/dist/ReactToastify.css';

interface SubjectProps {
  subjectCount: number;
  setSubjectCount: (value: number) => void;
  setActivePage: (page: string) => void;
}

interface SubjectItem {
  id: string;
  code: string;
  name: string;
  type: 'Theory' | 'Lab' | 'Embedded';
  credit: number;
  // labId removed here from usage in form and UI, but you can keep it if backend requires
  labId?: string;
}

const years = ['First Year', 'Second Year', 'Third Year', 'Fourth Year'];
const departments = ['CSE', 'ECE', 'EEE', 'MECH', 'CIVIL'];

const Subject: React.FC<SubjectProps> = () => {
  const [selectedYear, setSelectedYear] = useState('');
  const [selectedSemester, setSelectedSemester] = useState('');
  const [selectedDept, setSelectedDept] = useState('');
  const [semesters, setSemesters] = useState<string[]>([]);
  const [subjects, setSubjects] = useState<SubjectItem[]>([]);
  const [showForm, setShowForm] = useState(false);
  const [form, setForm] = useState<SubjectItem>({
    id: '',
    code: '',
    name: '',
    type: 'Theory',
    credit: 0,
    labId: '',
  });

  const username = localStorage.getItem('loggedUser') || '';
  const isAdmin = username.toLowerCase() === 'admin';

  useEffect(() => {
    if (!isAdmin) setSelectedDept(username);
  }, [username]);

  const handleYearChange = (value: string) => {
    setSelectedYear(value);
    setSelectedSemester('');
    switch (value) {
      case 'First Year':
        setSemesters(['I', 'II']);
        break;
      case 'Second Year':
        setSemesters(['III', 'IV']);
        break;
      case 'Third Year':
        setSemesters(['V', 'VI']);
        break;
      case 'Fourth Year':
        setSemesters(['VII', 'VIII']);
        break;
      default:
        setSemesters([]);
    }
  };

  const handleAddSubject = () => {
    if (!form.id || !form.code || !form.name || !form.credit) {
      toast.warning(' Please fill all subject fields!');
      return;
    }

    setSubjects([...subjects, form]);
    setForm({ id: '', code: '', name: '', type: 'Theory', credit: 0, labId: '' });
    setShowForm(false);
    toast.success('✅ Subject added!');
  };

  const handleSave = async () => {
    if (subjects.length === 0) {
      toast.warning(' No subjects to save!');
      return;
    }

    try {
      for (const subj of subjects) {
        const body = {
          subject_id: subj.id,
          sub_code: subj.code,
          subject_name: subj.name,
          year: selectedYear,
          sem: selectedSemester,
          department: selectedDept,
          department_id: selectedDept,
          subject_type: subj.type,
          credit: subj.credit,
          lab_id: subj.type === 'Lab' ? null : null, // lab_id always null since lab selection removed
        };

        const response = await fetch('https://localhost:7244/api/SubjectData/add', {
          method: 'POST',
          headers: { 'Content-Type': 'application/json' },
          body: JSON.stringify(body),
        });

        if (!response.ok) throw new Error(`❌ Failed to save: ${subj.name}`);
      }

      toast.success('🎉 All subjects saved successfully!');
      setSubjects([]);
      setForm({ id: '', code: '', name: '', type: 'Theory', credit: 0, labId: '' });
      setShowForm(true);
    } catch (error) {
      console.error('Save failed:', error);
      toast.error('❌ Error saving one or more subjects.');
    }
  };

  return (
    <div className="subject-wrapper1">
      <h2 className="grid-title">Subject Details</h2>

      <div className="subject-grid-row">
        <div className="subject-grid-item">
          <label className="subject-label">Year</label>
          <select value={selectedYear} onChange={(e) => handleYearChange(e.target.value)} className="dropdown">
            <option value="">Select</option>
            {years.map((year) => (
              <option key={year} value={year}>
                {year}
              </option>
            ))}
          </select>
        </div>

        <div className="subject-grid-item">
          <label className="subject-label">Semester</label>
          <select value={selectedSemester} onChange={(e) => setSelectedSemester(e.target.value)} className="dropdown">
            <option value="">Select</option>
            {semesters.map((sem) => (
              <option key={sem} value={sem}>
                {sem}
              </option>
            ))}
          </select>
        </div>

        <div className="subject-grid-item">
          <label className="subject-label">Department</label>
          {isAdmin ? (
            <select value={selectedDept} onChange={(e) => setSelectedDept(e.target.value)} className="dropdown">
              <option value="">Select</option>
              {departments.map((dept) => (
                <option key={dept} value={dept}>
                  {dept}
                </option>
              ))}
            </select>
          ) : (
            <input type="text" value={selectedDept} className="dropdown" readOnly />
          )}
        </div>
      </div>

      <div className="subject-add-btn-row">
        <button
          className="add-subject-btn"
          onClick={() => setShowForm(!showForm)}
          disabled={!selectedYear || !selectedDept || !selectedSemester}
        >
          {showForm ? 'Cancel' : 'Add Subject'}
        </button>
      </div>

      {showForm && (
        <div className="subject-form-row">
          <div className="subject-form-item">
            <label className="subject-label1">Subject ID</label>
            <input
              className="subject-input"
              type="text"
              value={form.id}
              onChange={(e) => setForm({ ...form, id: e.target.value })}
            />
          </div>
          <div className="subject-form-item">
            <label className="subject-label1">Subject Code</label>
            <input
              className="subject-input"
              type="text"
              value={form.code}
              onChange={(e) => setForm({ ...form, code: e.target.value })}
            />
          </div>
          <div className="subject-form-item">
            <label className="subject-label1">Subject Name</label>
            <input
              className="subject-input"
              type="text"
              value={form.name}
              onChange={(e) => setForm({ ...form, name: e.target.value })}
            />
          </div>
          <div className="subject-form-item">
            <label className="subject-label1">Credit</label>
            <input
              className="subject-input"
              type="number"
              min={1}
              value={form.credit === 0 ? '' : form.credit}
              onChange={(e) => setForm({ ...form, credit: Number(e.target.value) })}
            />
          </div>
        <div className="subject-form-item1 subject-type-group">
  <label className="subject-label">Subject Type</label>
  <div className="radio-group">
    {['Theory', 'Lab', 'Embedded'].map((type) => (
      <label key={type}>
        <input
          type="radio"
          style={{ width: '20px', height: '20px' }}
          name="type"
          value={type}
          checked={form.type === type}
          onChange={(e) =>
            setForm({ ...form, type: e.target.value as SubjectItem['type'], labId: '' })
          }
        />
        {type}
      </label>
    ))}
  </div>
</div>

          <div className="subject-form-item">
            <button className="save-btn" onClick={handleAddSubject}>
              Add
            </button>
          </div>
        </div>
      )}

      {subjects.length > 0 && (
        <div className="subject-list">
          <h3>Added Subjects</h3>
          <table>
            <thead>
              <tr>
                <th>Subject ID</th>
                <th>Code</th>
                <th>Name</th>
                <th>Type</th>
                <th>Credit</th>
                {/* Removed Lab ID column */}
              </tr>
            </thead>
            <tbody>
              {subjects.map((subj, idx) => (
                <tr key={idx}>
                  <td>{subj.id}</td>
                  <td>{subj.code}</td>
                  <td>{subj.name}</td>
                  <td>{subj.type}</td>
                  <td>{subj.credit}</td>
                  {/* Removed Lab ID cell */}
                </tr>
              ))}
            </tbody>
          </table>
          <button className="save-btn" onClick={handleSave}>
            Save
          </button>
        </div>
      )}

      <ToastContainer position="top-right" autoClose={3000} hideProgressBar />
    </div>
  );
};

export default Subject;
