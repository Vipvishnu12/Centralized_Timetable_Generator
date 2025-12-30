import React, { useEffect, useState } from "react";
import "../styles/Staff.css";

interface StaffRecord {
  staffId: string;
  name: string;
  subject1: string;
  subject2: string;
  subject3: string;
}

const ViewStaff: React.FC = () => {
  const [viewDept, setViewDept] = useState("");
  const [existingStaff, setExistingStaff] = useState<StaffRecord[]>([]);
  const [editIndex, setEditIndex] = useState<number | null>(null);
  const [editedRecord, setEditedRecord] = useState<StaffRecord | null>(null);

  useEffect(() => {
    const loggedUser = localStorage.getItem("loggedUser") || "";
    setViewDept(loggedUser.toUpperCase());
    fetchStaff(loggedUser.toUpperCase());
  }, []);

  const fetchStaff = async (dept: string) => {
    try {
      const response = await fetch(`https://localhost:7244/api/StaffData/department/${dept}`);
      const data = await response.json();
      setExistingStaff(data || []);
    } catch (error) {
      console.error("Error fetching staff:", error);
    }
  };

  const handleEdit = (index: number) => {
    setEditIndex(index);
    setEditedRecord({ ...existingStaff[index] });
  };

  const handleChange = (field: keyof StaffRecord, value: string) => {
    if (!editedRecord) return;
    setEditedRecord({ ...editedRecord, [field]: value });
  };

  const handleSave = async () => {
    if (!editedRecord) return;
console.log(editedRecord);
    try {
      const response = await fetch("https://localhost:7244/api/CrossDepartmentAssignments/update", {
        method: "PUT",
        headers: { "Content-Type": "application/json" },
        body: JSON.stringify(editedRecord),
      });

      const result = await response.json();
      alert(result.message || "✅ Staff record updated successfully");

      const updated = [...existingStaff];
      if (editIndex !== null) updated[editIndex] = editedRecord;
      setExistingStaff(updated);

      setEditIndex(null);
      setEditedRecord(null);
    } catch (err) {
      alert("❌ Failed to update staff record");
      console.error(err);
    }
  };

  const handleCancel = () => {
    setEditIndex(null);
    setEditedRecord(null);
  };

  return (
    <div className="staff-table-wrapper">
      <h2 className="grid-title1">View Staff</h2>

      <div style={{ marginBottom: "20px", marginLeft: "20px", display: "flex", flexDirection: "column", gap: "20px" }}>
        <label>Department ID:</label>
        <input
          type="text"
          value={viewDept}
          readOnly
          style={{ padding: "8px", width: "250px", backgroundColor: "#f0f0f0" }}
        />
      </div>

      {existingStaff.length > 0 && (
        <>
          <h3>Existing Staff</h3>
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
                  <th>Action</th>
                </tr>
              </thead>
              <tbody>
                {existingStaff.map((staff, index) => {
                  const isEditing = index === editIndex;
                  const record = isEditing ? editedRecord : staff;

                  return (
                    <tr key={index} className={index % 2 === 0 ? "row-white" : "row-grey"}>
                      <td>{index + 1}</td>
                      <td>
                        <input type="text" value={record?.staffId || ""} readOnly />
                      </td>
                      <td>
                        <input
                          type="text"
                          value={record?.name || ""}
                          readOnly={!isEditing}
                          onChange={(e) => handleChange("name", e.target.value)}
                        />
                      </td>
                      <td>
                        <input
                          type="text"
                          value={record?.subject1 || ""}
                          readOnly={!isEditing}
                          onChange={(e) => handleChange("subject1", e.target.value)}
                        />
                      </td>
                      <td>
                        <input
                          type="text"
                          value={record?.subject2 || ""}
                          readOnly={!isEditing}
                          onChange={(e) => handleChange("subject2", e.target.value)}
                        />
                      </td>
                      <td>
                        <input
                          type="text"
                          value={record?.subject3 || ""}
                          readOnly={!isEditing}
                          onChange={(e) => handleChange("subject3", e.target.value)}
                        />
                      </td>
                      <td className="action-buttons">
                        {isEditing ? (
                          <>
                            <button onClick={handleSave} className="save-button">Save</button>
                            <button onClick={handleCancel} className="cancel-button">Cancel</button>
                          </>
                        ) : (
                          <button onClick={() => handleEdit(index)} className="edit-button">Edit</button>
                        )}
                      </td>
                    </tr>
                  );
                })}
              </tbody>
            </table>
          </div>
        </>
      )}
    </div>
  );
};

export default ViewStaff;
