import React, { useState, useEffect } from 'react';
import '../styles/Staff.css';
import { toast, ToastContainer } from 'react-toastify';
import 'react-toastify/dist/ReactToastify.css';

interface StaffProps {
  totalStaff: number;
  departmentData: {
    department: string;
    departmentName: string;
    block: string;
  };
}

const Staff: React.FC<StaffProps> = ({ totalStaff, departmentData }) => {
  const [staffDetails, setStaffDetails] = useState<any[]>([]);

  useEffect(() => {
    const fetchStaffCount = async () => {
      try {
        const response = await fetch(
          `https://localhost:7244/api/StaffData/count/${departmentData.department}`
        );
        const data = await response.json();
        const initialCount = data.count || 0;

        const generated = Array.from({ length: totalStaff }, (_, index) => {
          const id = `${departmentData.department}${initialCount + index + 1}`;
          return {
            staffId: id,
            name: '',
            subject1: '',
            subject2: '',
            subject3: '',
          };
        });

        setStaffDetails(generated);
      } catch (error) {
        console.error('Error fetching count:', error);
        toast.error('❌ Failed to fetch staff count.');
      }
    };

    fetchStaffCount();
  }, [totalStaff, departmentData]);

  const handleInputChange = (index: number, field: string, value: string) => {
    const updated = [...staffDetails];
    updated[index][field] = value;
    setStaffDetails(updated);
  };

  const handleSubmit = async () => {
    const finalData = staffDetails.map((staff) => ({
      staffId: staff.staffId,
      name: staff.name,
      subject1: staff.subject1,
      subject2: staff.subject2,
      subject3: staff.subject3,
      block: departmentData.block,
      department: departmentData.departmentName,
      department_id: departmentData.department,
    }));

    try {
      const response = await fetch('https://localhost:7244/api/StaffData/add', {
        method: 'POST',
        headers: {
          'Content-Type': 'application/json',
        },
        body: JSON.stringify(finalData),
      });

      if (response.ok) {
        toast.success('✅ Staff data saved successfully!');
      } else {
        toast.error('❌ Failed to save staff data.');
      }
    } catch (error) {
      console.error('Error during submission:', error);
      toast.error('❌ Something went wrong while saving data.');
    }
  };

  return (
    <div className="staff-table-wrapper">
      <h2 className="grid-title1">Staff Details</h2>

      {totalStaff > 0 ? (
        <>
          <div className="staff-table-header">
            <div className="table-wrapper1">
              <table className="staff-table">
                <thead>
                  <tr>
                    <th>S.No</th>
                    <th>Staff ID</th>
                    <th>Staff Name</th>
                    <th>Subject 1</th>
                    <th>Subject 2</th>
                    <th>Subject 3</th>
                  </tr>
                </thead>
                <tbody>
                  {staffDetails.map((staff, index) => (
                    <tr key={index} className={index % 2 === 0 ? 'row-white' : 'row-grey'}>
                      <td>{index + 1}</td>
                      <td>
                        <input type="text" value={staff.staffId} readOnly />
                      </td>
                      <td>
                        <input
                          type="text"
                          value={staff.name}
                          onChange={(e) =>
                            handleInputChange(index, 'name', e.target.value.toUpperCase())
                          }
                          placeholder="Enter Name"
                        />
                      </td>
                      <td>
                        <input
                          type="text"
                          value={staff.subject1}
                          onChange={(e) =>
                            handleInputChange(index, 'subject1', e.target.value.toUpperCase())
                          }
                          placeholder="Subject 1"
                        />
                      </td>
                      <td>
                        <input
                          type="text"
                          value={staff.subject2}
                          onChange={(e) =>
                            handleInputChange(index, 'subject2', e.target.value.toUpperCase())
                          }
                          placeholder="Subject 2"
                        />
                      </td>
                      <td>
                        <input
                          type="text"
                          value={staff.subject3}
                          onChange={(e) =>
                            handleInputChange(index, 'subject3', e.target.value.toUpperCase())
                          }
                          placeholder="Subject 3"
                        />
                      </td>
                    </tr>
                  ))}
                </tbody>
              </table>
            </div>

            <div className="save-button-container">
              <button className="g-button" onClick={handleSubmit}>
                Save
              </button>
            </div>
          </div>
        </>
      ) : (
        <p>No staff to display. Please enter a value in Department page.</p>
      )}

      <ToastContainer position="top-right" autoClose={3000} hideProgressBar />
    </div>
  );
};

export default Staff;
