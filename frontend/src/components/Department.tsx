import React, { useState, useEffect } from 'react';
import { ToastContainer, toast } from 'react-toastify';
import 'react-toastify/dist/ReactToastify.css';
import '../styles/Department.css';

interface DepartmentProps {
  totalStaff: number;
  setTotalStaff: (value: number) => void;
  setDepartmentData?: (data: {
    department: string;
    departmentName: string;
    block: string;
  }) => void;
  onShowStaff: () => void;
}

const Department: React.FC<DepartmentProps> = ({
  totalStaff,
  setTotalStaff,
  setDepartmentData,
  onShowStaff,
}) => {
  const [department, setDepartment] = useState('');
  const [departmentName, setDepartmentName] = useState('');
  const [block, setBlock] = useState('');
  const [isAdmin, setIsAdmin] = useState(false);

  useEffect(() => {
    const loggedUser = localStorage.getItem('loggedUser') || '';
    const isUserAdmin = loggedUser.toLowerCase() === 'admin';

    setIsAdmin(isUserAdmin);

    if (!isUserAdmin) {
      setDepartment(loggedUser);
      setDepartmentName(loggedUser);
    }
  }, []);

  const handleNext = () => {
    // For admin, block is not required
    if (!department || !departmentName || (!isAdmin && !block) || !totalStaff) {
      toast.warn('⚠️ Please fill all fields!');
      return;
    }

    setDepartmentData?.({
      department,
      departmentName,
      block: isAdmin ? '' : block,
    });

    toast.success('✅ Department data saved!');
    setTimeout(() => {
      onShowStaff(); // Navigate to next step
    }, 1000);
  };

  return (
    <div className="department-wrapper">
      <h2 className="grid-title">Department Details</h2>

      <div className="department-form-row">
        <div className="form-item">
          <label htmlFor="department" className="department-label">Department ID</label>
          <input
            type="text"
            id="department"
            value={department}
            onChange={(e) => setDepartment(e.target.value.toUpperCase())}
            disabled={!isAdmin}
            placeholder={isAdmin ? "Enter department ID" : ""}
          />
        </div>

        <div className="form-item">
          <label htmlFor="departmentName" className="department-label">Department Name</label>
          <input
            type="text"
            id="departmentName"
            value={departmentName}
            onChange={(e) => setDepartmentName(e.target.value.toUpperCase())}
            disabled={!isAdmin}
            placeholder={isAdmin ? "Enter department name" : ""}
          />
        </div>

        {/* Block field: only for USER */}
        {!isAdmin && (
          <div className="form-item">
            <label htmlFor="block" className="department-label">Block</label>
            <input
              type="text"
              id="block"
              value={block}
              onChange={(e) => setBlock(e.target.value.toUpperCase())}
              placeholder="Enter block name"
            />
          </div>
        )}

        <div className="form-item">
          <label htmlFor="totalStaff" className="department-label">Total Staff</label>
          <input
            type="number"
            id="totalStaff"
            value={totalStaff === 0 ? '' : totalStaff}
            onChange={(e) => setTotalStaff(Number(e.target.value))}
            placeholder="Enter total staff"
            min={1}
          />
        </div>
      </div>

      <div className="button-container">
        <button className="grid-button" onClick={handleNext}>Next</button>
      </div>

      {/* Toast Notifications */}
      <ToastContainer position="top-right" autoClose={3000} hideProgressBar />
    </div>
  );
};

export default Department;