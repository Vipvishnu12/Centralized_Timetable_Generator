import React, { useState, useEffect } from 'react';
import { ToastContainer, toast } from 'react-toastify';
import 'react-toastify/dist/ReactToastify.css';
import '../styles/LabCreation.css';

interface LabCreationProps {
  onLabSave?: (data: {
    labId: string;
    labName: string;
    department: string;
    systems: string;
    Block: string;
  }) => void;
}

// Helper function to check if a string is numeric
const isNumeric = (value: string) => /^\d+$/.test(value);

const LabCreation: React.FC<LabCreationProps> = ({ onLabSave }) => {
  const [labId, setLabId] = useState('');
  const [labName, setLabName] = useState('');
  const [department, setDepartment] = useState('');
  const [systems, setSystems] = useState('');
  const [Block, setBlock] = useState('');
  const [isAdmin, setIsAdmin] = useState(false);

  useEffect(() => {
    const loggedUser = localStorage.getItem('loggedUser') || '';
    const isUserAdmin = loggedUser.toLowerCase() === 'admin';
    setIsAdmin(isUserAdmin);

    if (!isUserAdmin) {
      setDepartment(loggedUser);
    }
  }, []);

  const handleSave = async () => {
    if (
      !labId.trim() ||
      !labName.trim() ||
      !department.trim() ||
      !systems.trim() ||
      !Block.trim() ||
      !isNumeric(systems.trim())
    ) {
      toast.warn('⚠️ Please fill all fields correctly!');
      return;
    }

    const labData = {
      labId,
      labName,
      department,
      systems,
      Block,
    };

    try {
      const response = await fetch('https://localhost:7244/api/Lab/create', {
        method: 'POST',
        headers: {
          'Content-Type': 'application/json',
        },
        body: JSON.stringify(labData),
      });

      if (!response.ok) {
        const errorData = await response.json();
        throw new Error(errorData.message || 'Failed to save lab');
      }

      toast.success('✅ Lab created successfully!');
      onLabSave?.(labData);

      // Clear form
      setLabId('');
      setLabName('');
      setSystems('');
      setBlock('');
    } catch (err: any) {
      console.error(err);
      toast.error(`❌ ${err.message}`);
    }
  };

  return (
    <div className="lab-wrapper">
      <h2 className="grid-title">Lab Creation</h2>

      <div className="lab-form-row">
        <div className="form-item3">
          <label htmlFor="labId" className="lab-label">Lab ID</label>
          <input
            type="text"
            id="labId"
            value={labId}
            onChange={(e) => setLabId(e.target.value.toUpperCase())}
            placeholder="Enter Lab ID"
          />
        </div>

        <div className="form-item3">
          <label htmlFor="labName" className="lab-label">Lab Name</label>
          <input
            type="text"
            id="labName"
            value={labName}
            onChange={(e) => setLabName(e.target.value.toUpperCase())}
            placeholder="Enter Lab Name"
          />
        </div>

        <div className="form-item3">
          <label htmlFor="department" className="lab-label">Department</label>
          <input
            type="text"
            id="department"
            value={department}
            onChange={(e) => setDepartment(e.target.value.toUpperCase())}
            disabled={!isAdmin}
            placeholder={isAdmin ? "Enter Department" : ""}
          />
        </div>

        <div className="form-item3">
          <label htmlFor="systems" className="lab-label">No. of Systems</label>
          <input
            type="text"
            id="systems"
            value={systems}
            onChange={(e) => setSystems(e.target.value)}
            placeholder="Enter total systems"
          />
        </div>

        <div className="form-item3">
          <label htmlFor="Block" className="lab-label">Block</label>
          <input
            type="text"
            id="Block"
            value={Block}
            onChange={(e) => setBlock(e.target.value.toUpperCase())}
            placeholder="Enter Block"
          />
        </div>
      </div>

      <div className="button-container">
        <button className="grid-button" onClick={handleSave}>Create Lab</button>
      </div>

      <ToastContainer position="top-right" autoClose={3000} hideProgressBar />
    </div>
  );
};

export default LabCreation;
