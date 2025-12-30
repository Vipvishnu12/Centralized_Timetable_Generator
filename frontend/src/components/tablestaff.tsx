import React, { useRef, useState, useEffect } from "react";
import Select from "react-select";
import html2pdf from "html2pdf.js";
import { ToastContainer, toast } from "react-toastify";
import 'react-toastify/dist/ReactToastify.css';
import "../styles/tablestaff.css";

interface TimetableRecord {
  SubjectId: string;
  SubjectCode: string;
  Year: string;
  Section: string;
  Day: string;
  Hour: number;
  Department: string;
}

interface FreezeHourData {
  staffId: string;
  staffName: string;
  department: string;
  day: string;
  hour: number;
  year: string;
  section: string;
  reasonCode: string;
  reasonFull: string;
}

const days = ["Mon", "Tue", "Wed", "Thu", "Fri"];
const periods = [1, 2, 3, 4, 5, 6, 7];

const StaffTable: React.FC = () => {
  const tableRef = useRef<HTMLDivElement>(null);
  const [staffIdInput, setStaffIdInput] = useState("");
  const [staffData, setStaffData] = useState<TimetableRecord[]>([]);
  const [loading, setLoading] = useState(false);
  const [staffId, setStaffId] = useState("");
  const [staffName, setStaffName] = useState("");
  const [staffList, setStaffList] = useState<{ staffId: string; staffName: string }[]>([]);
  const [department, setDepartment] = useState("");
  const [allDepartments, setAllDepartments] = useState<string[]>([]);
  
  // Modal state
  const [showFreezeModal, setShowFreezeModal] = useState(false);
  const [freezeData, setFreezeData] = useState<FreezeHourData>({
    staffId: "",
    staffName: "",
    department: "",
    day: "",
    hour: 0,
    year: "",
    section: "",
    reasonCode: "",
    reasonFull: ""
  });
  const [frozenSlots, setFrozenSlots] = useState<Set<string>>(new Set());
  const [isExporting, setIsExporting] = useState(false);

  useEffect(() => {
    const loggedUser = localStorage.getItem("loggedUser") || "";
    setDepartment(loggedUser);
    if (loggedUser.toLowerCase() === "admin") {
      fetch("https://localhost:7244/api/Login/departments")
        .then((res) => res.json())
        .then((data) => setAllDepartments(data))
        .catch(() => toast.error("Error loading departments"));
    } else {
      fetchStaffList(loggedUser);
    }
  }, []);

  const fetchStaffList = async (dept: string) => {
    try {
      const res = await fetch(
        `https://localhost:7244/api/StaffSubject/staff?departmentId=${encodeURIComponent(dept)}`
      );
      if (!res.ok) {
        throw new Error("Failed to fetch staff list");
      }
      const data = await res.json();
      setStaffList(data);
    } catch (error) {
      console.error("Error fetching staff list:", error);
      toast.error("Failed to load staff list");
      setStaffList([]);
    }
  };

  const handleDepartmentChange = (e: React.ChangeEvent<HTMLSelectElement>) => {
    const selectedDept = e.target.value;
    setDepartment(selectedDept);
    setStaffList([]);
    setStaffIdInput("");
    fetchStaffList(selectedDept);
  };

  const handleFreezeClick = (day: string, hour: number) => {
    setFreezeData({
      staffId: staffId,
      staffName: staffName,
      department: department,
      day: day,
      hour: hour,
      year: "",
      section: "",
      reasonCode: "",
      reasonFull: ""
    });
    setShowFreezeModal(true);
  };

  const handleFreezeSubmit = async (e: React.FormEvent) => {
    e.preventDefault();
    
    if (!freezeData.staffId || !freezeData.day || !freezeData.hour) {
      toast.warn("⚠️ Please fill in required fields");
      return;
    }

    try {
      const response = await fetch('https://localhost:7244/api/StaffSubject/freezeHour', {
        method: 'POST',
        headers: {
          'Content-Type': 'application/json',
        },
        body: JSON.stringify({
          staffId: freezeData.staffId,
          day: freezeData.day,
          hour: freezeData.hour,
          department: freezeData.department,
          year: freezeData.year,
          section: freezeData.section,
          reasonCode: freezeData.reasonCode,
          reasonFull: freezeData.reasonFull
        }),
      });

      if (!response.ok) {
        throw new Error('Failed to freeze hour');
      }

      const slotKey = `${freezeData.day}-${freezeData.hour}`;
      setFrozenSlots(prev => new Set([...prev, slotKey]));
      setShowFreezeModal(false);
      toast.success("🧊 Hour frozen successfully!");
      
      // Reset form
      setFreezeData({
        staffId: "",
        staffName: "",
        department: "",
        day: "",
        hour: 0,
        year: "",
        section: "",
        reasonCode: "",
        reasonFull: ""
      });
    } catch (error) {
      console.error('Error freezing hour:', error);
      toast.error("❌ Failed to freeze hour");
    }
  };

  const closeFreezeModal = () => {
    setShowFreezeModal(false);
    setFreezeData({
      staffId: "",
      staffName: "",
      department: "",
      day: "",
      hour: 0,
      year: "",
      section: "",
      reasonCode: "",
      reasonFull: ""
    });
  };

  const handleExportPDF = async () => {
    if (!tableRef.current) return;
    
    setIsExporting(true);
    
    // Wait for state to update and DOM to re-render
    setTimeout(async () => {
      try {
        await html2pdf()
          .set({
            filename: "staff-timetable.pdf",
            image: { type: "jpeg", quality: 0.98 },
            html2canvas: { scale: 2 },
            jsPDF: { unit: "in", format: "a4", orientation: "portrait" },
          })
          .from(tableRef.current!)
          .save();
        toast.success("📄 Timetable exported as PDF");
      } catch (error) {
        toast.error("❌ Failed to export PDF");
        console.error("PDF export error:", error);
      } finally {
        setIsExporting(false);
      }
    }, 100);
  };

  const handleFetchStaffTimetable = async () => {
    if (!staffIdInput.trim()) {
      toast.warn("⚠️ Please select a staff member");
      return;
    }

    setLoading(true);
    setStaffData([]);
    setStaffId("");
    setStaffName("");

    try {
      const resp = await fetch(
        `https://localhost:7244/api/CrossDepartmentAssignments/getStaffTimetableById?staffId=${encodeURIComponent(
          staffIdInput.trim()
        )}`
      );

      if (!resp.ok) {
        const errorData = await resp.json();
        throw new Error(errorData?.message || "Failed to fetch timetable");
      }

      const data = await resp.json();

      if (!data || !Array.isArray(data.records)) {
        throw new Error("Invalid data format received");
      }

      const mapped = data.records.map((r: any) => ({
        SubjectId: r.subjectId || r.SubjectId || "N/A",
        SubjectCode: r.subjectCode || r.SubjectCode || "",
        Year: r.year || "",
        Section: r.section || "",
        Day: r.day || "",
        Hour: Number(r.hour) || 0,
        Department: r.department || "",
      }));

      setStaffId(data.staffId);
      setStaffName(data.staffName);
      setStaffData(mapped);
      toast.success("✅ Timetable loaded successfully");
    } catch (e: any) {
      console.error("Error fetching timetable:", e);
      toast.error(`❌ ${e.message || "Failed to load timetable"}`);
    } finally {
      setLoading(false);
    }
  };

  const buildSlotMap = () => {
    const map: Record<string, Record<number, string>> = {};

    for (const r of staffData) {
      if (!r.Day || typeof r.Day !== "string" || typeof r.Hour !== "number") continue;
      const day = r.Day.charAt(0).toUpperCase() + r.Day.slice(1).toLowerCase();
      if (!map[day]) map[day] = {};
      const display = r.SubjectCode
        ? `${r.SubjectCode} (${r.Department}, ${r.Year}-${r.Section})`
        : `${r.SubjectId} (${r.Department}, ${r.Year}-${r.Section})`;
      map[day][r.Hour] = display;
    }
    return map;
  };

  const slotMap = buildSlotMap();

  return (
    <div className="staff-table-container">
      <ToastContainer position="top-right" autoClose={3000} hideProgressBar />

      <div className="staff-controls">
        {localStorage.getItem("loggedUser")?.toLowerCase() === "admin" && (
          <select value={department} onChange={handleDepartmentChange}>
            <option value="">Select Department</option>
            {allDepartments.map((dept) => (
              <option key={dept} value={dept}>
                {dept}
              </option>
            ))}
          </select>
        )}

        <div style={{ minWidth: 300 }}>
          <Select
            options={staffList.map((s) => ({
              value: s.staffId,
              label: `${s.staffName} (${s.staffId})`,
            }))}
            value={
              staffIdInput
                ? staffList
                    .map((s) => ({ value: s.staffId, label: `${s.staffName} (${s.staffId})` }))
                    .find((opt) => opt.value === staffIdInput)
                : null
            }
            onChange={(selected: { value: string; label: string } | null) => setStaffIdInput(selected ? selected.value : "")}
            placeholder="Select Staff..."
            isClearable
            isSearchable
          />
        </div>

        <button onClick={handleFetchStaffTimetable} disabled={loading || !staffIdInput}>
          {loading ? "⌛ Loading..." : "📥 View Staff Timetable"}
        </button>
      </div>

      {staffData.length > 0 && (
        <div className="staff-name-heading">
          <h3>
            👨‍🏫 Staff Name: <span style={{ color: "#2a6" }}>{staffName}</span>
          </h3>
        </div>
      )}

      {staffData.length > 0 && (
        <div ref={tableRef}>
          <table className="staff-timetable">
            <thead>
              <tr>
                <th>Day</th>
                {periods.map((p) => (
                  <th key={p}>Period {p}</th>
                ))}
              </tr>
            </thead>
            <tbody>
              {days.map((day) => (
                <tr key={day}>
                  <td className="staff-day-header">{day}</td>
                  {periods.map((hour) => {
                    const slotContent = slotMap[day]?.[hour];
                    const slotKey = `${day}-${hour}`;
                    const isFrozen = frozenSlots.has(slotKey);
                    
                    return (
                      <td key={hour} className={isFrozen ? "frozen-slot" : ""}>
                        {slotContent ? (
                          slotContent
                        ) : isFrozen ? (
                          <span className="frozen-indicator">🧊 Frozen</span>
                        ) : (
                          <div className="empty-slot">
                            <span>---</span>
                            {!isExporting && (
                              <button 
                                className="freeze-btn"
                                onClick={() => handleFreezeClick(day, hour)}
                                title="Freeze this hour"
                              >
                                🧊
                              </button>
                            )}
                          </div>
                        )}
                      </td>
                    );
                  })}
                </tr>
              ))}
            </tbody>
          </table>
        </div>
      )}

      {staffData.length > 0 && (
        <div className="staff-button-group">
          <button className="staff-export-btn" onClick={handleExportPDF}>
            📤 Export to PDF
          </button>
        </div>
      )}

      {/* Freeze Hour Modal */}
      {showFreezeModal && (
        <div className="modal-overlay" onClick={closeFreezeModal}>
          <div className="modal-content" onClick={(e) => e.stopPropagation()}>
            <div className="modal-header">
              <h3>🧊 Freeze Hour</h3>
              <button className="modal-close" onClick={closeFreezeModal}>×</button>
            </div>
            
            <form onSubmit={handleFreezeSubmit}>
              <div className="form-group">
                <label>Staff ID *</label>
                <input
                  type="text"
                  value={freezeData.staffId}
                  onChange={(e) => setFreezeData({...freezeData, staffId: e.target.value})}
                  required
                  readOnly
                />
              </div>

              <div className="form-group">
                <label>Staff Name *</label>
                <input
                  type="text"
                  value={freezeData.staffName}
                  onChange={(e) => setFreezeData({...freezeData, staffName: e.target.value})}
                  required
                  readOnly
                />
              </div>

              <div className="form-group">
                <label>Department *</label>
                <input
                  type="text"
                  value={freezeData.department}
                  onChange={(e) => setFreezeData({...freezeData, department: e.target.value})}
                  required
                  readOnly
                />
              </div>

              <div className="form-row">
                <div className="form-group">
                  <label>Day *</label>
                  <input
                    type="text"
                    value={freezeData.day}
                    readOnly
                  />
                </div>

                <div className="form-group">
                  <label>Hour (Period) *</label>
                  <input
                    type="number"
                    value={freezeData.hour}
                    readOnly
                  />
                </div>
              </div>

              <div className="form-row">
                <div className="form-group">
                  <label>Year</label>
                  <input
                    type="text"
                    value={freezeData.year}
                    onChange={(e) => setFreezeData({...freezeData, year: e.target.value})}
                    placeholder="e.g., I, II, III, IV"
                  />
                </div>

                <div className="form-group">
                  <label>Section</label>
                  <input
                    type="text"
                    value={freezeData.section}
                    onChange={(e) => setFreezeData({...freezeData, section: e.target.value})}
                    placeholder="e.g., A, B, C"
                  />
                </div>
              </div>

              <div className="form-group">
                <label>Reason Short Code *</label>
                <select
                  value={freezeData.reasonCode}
                  onChange={(e) => setFreezeData({...freezeData, reasonCode: e.target.value})}
                  required
                >
                  <option value="">Select Reason Code</option>
                  <option value="ADMIN">ADMIN</option>
                  <option value="REVIEW">REVIEW</option>
                  <option value="MEETING">MEETING</option>
                  <option value="OTHER">OTHER</option>
                </select>
              </div>

              <div className="form-group">
                <label>Full Reason Description</label>
                <textarea
                  value={freezeData.reasonFull}
                  onChange={(e) => setFreezeData({...freezeData, reasonFull: e.target.value})}
                  placeholder="Optional: Add a detailed description for freezing this hour"
                  rows={3}
                />
              </div>

              <div className="modal-actions">
                <button type="button" onClick={closeFreezeModal} className="btn-cancel">
                  Cancel
                </button>
                <button type="submit" className="btn-freeze">
                  🧊 Freeze Hour
                </button>
              </div>
            </form>
          </div>
        </div>
      )}
    </div>
  );
};

export default StaffTable;
