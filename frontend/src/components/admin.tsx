import React, { useState, useEffect } from 'react';
import '../styles/Department.css';
import '../assets/nandha logo (1).svg';
import { ToastContainer, toast } from 'react-toastify';
import 'react-toastify/dist/ReactToastify.css';

const Admin: React.FC = () => {
  const [departmentId, setDepartmentId] = useState('');
  const [departmentName, setDepartmentName] = useState('');
  const [password, setPassword] = useState('');

  useEffect(() => {
    const loggedUser = localStorage.getItem('loggedUser') || '';
    if (loggedUser.toLowerCase() !== 'admin') {
      toast.error('Unauthorized access!');
    }
  }, []);

  const handlePasswordFocus = () => {
    if (!password && departmentId) {
      const generated = departmentId.toLowerCase() + '123';
      setPassword(generated);
    }
  };

  const handleSubmit = async () => {
    if (!departmentId || !departmentName || !password) {
      toast.warning('Please fill all fields!');
      return;
    }
var role="user";
    try {
      const response = await fetch('https://localhost:7244/api/Login/add-department', {
        method: 'POST',
        headers: {
          'Content-Type': 'application/json'
        },
        body: JSON.stringify({
          departmentId,
          departmentName,
          password,role
        })
      });

      if (response.ok) {
        await response.json();
        toast.success('✅ Department created successfully!');
        setDepartmentId('');
        setDepartmentName('');
        setPassword('');
      } else {
        const err = await response.json();
        toast.error('❌ Error: ' + err.message);
      }
    } catch (err) {
      console.error(err);
      toast.error('❌ Failed to connect to the server.');
    }
  };

  return (
    <div className="department-wrapper">
      <h2 className="grid-title">Create New Department</h2>

      <div className="department-form-row">
        <div className="form-item">
          <label htmlFor="departmentId" className="department-label">Department ID</label>
          <input
            type="text"
            id="departmentId"
            value={departmentId}
            onChange={(e) => setDepartmentId(e.target.value.toUpperCase())}
            placeholder="e.g., CSE"
          />
        </div>

        <div className="form-item">
          <label htmlFor="departmentName" className="department-label">Department Name</label>
          <input
            type="text"
            id="departmentName"
            value={departmentName}
            onChange={(e) => setDepartmentName(e.target.value.toUpperCase())}
            placeholder="e.g., COMPUTER SCIENCE"
          />
        </div>

        <div className="form-item">
          <label htmlFor="password" className="department-label">Password</label>
          <input
            type="text"
            id="password"
            value={password}
            onFocus={handlePasswordFocus}
            onChange={(e) => setPassword(e.target.value)}
            placeholder="Generate or enter password"
          />
        </div>
      </div>

      <div className="button-container">
        <button className="grid-button" onClick={handleSubmit}>Create Department</button>
      </div>

      <ToastContainer position="top-right" autoClose={3000} hideProgressBar={false} />
    </div>
  );
};

export default Admin;
