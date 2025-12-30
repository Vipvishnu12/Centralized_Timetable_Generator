import React, { useEffect, useRef, useState } from "react";
import html2pdf from "html2pdf.js";
import { ToastContainer, toast } from "react-toastify";
import 'react-toastify/dist/ReactToastify.css';
import "../styles/tablestaff.css";

interface TimetableRecord {
  subjectId: string;
  subjectCode: string;
  staff: string;
  department: string;
  year: string;
  semester: string;
  section: string;
  day: string;
  hour: number;
}

interface LabInfo {
  labId: string;
  labName: string;
  department: string;
  systems: number;
}

const days = ["Mon", "Tue", "Wed", "Thu", "Fri"];
const periods = [1, 2, 3, 4, 5, 6, 7];

const LabTable: React.FC = () => {
  const tableRef = useRef<HTMLDivElement>(null);
  const [labData, setLabData] = useState<TimetableRecord[]>([]);
  const [labId, setLabId] = useState("");
  const [labName, setLabName] = useState("");
  const [loading, setLoading] = useState(false);
  const [labs, setLabs] = useState<LabInfo[]>([]);
  const [labIdInput, setLabIdInput] = useState(""); // selected Lab ID from dropdown

  const loggedUser = localStorage.getItem("loggedUser") || "";

  // ✅ Fetch all labs from backend and filter based on user
  const fetchLabs = async () => {
    try {
      const res = await fetch(`https://localhost:7244/api/Lab/all1`);
      const data: LabInfo[] = await res.json();
      if (!res.ok) throw new Error("Failed to fetch labs");

      if (loggedUser.toLowerCase() === "admin") {
        setLabs(data);
      } else {
        const filtered = data.filter((lab) => lab.department === loggedUser);
        setLabs(filtered);
      }
    } catch (error) {
      console.error("Error fetching labs:", error);
      toast.error("❌ Failed to load lab list");
    }
  };

  const handleExportPDF = () => {
    if (!tableRef.current) return;
    html2pdf()
      .set({
        filename: "lab-timetable.pdf",
        image: { type: "jpeg", quality: 0.98 },
        html2canvas: { scale: 2 },
        jsPDF: { unit: "in", format: "a4", orientation: "portrait" },
      })
      .from(tableRef.current)
      .save();
    toast.success("📄 Lab timetable exported as PDF");
  };

  const handleFetchLabTimetable = async () => {
    if (!labIdInput.trim()) {
      toast.warn("⚠️ Please select a Lab ID");
      return;
    }

    setLoading(true);
    setLabData([]);
    setLabId("");
    setLabName("");

    try {
      const resp = await fetch(
        `https://localhost:7244/api/Lab/getTimetableByLabId?labId=${encodeURIComponent(
          labIdInput.trim()
        )}`
      );
      const data = await resp.json();

      if (!resp.ok) throw new Error(data?.message || "Failed to fetch lab timetable");

      const mapped: TimetableRecord[] = data.records.map((r: any) => ({
        subjectId: r.subjectId,
        subjectCode: r.subjectCode,
        staff: r.staff,
        department: r.department,
        year: r.year,
        semester: r.semester,
        section: r.section,
        day: r.day,
        hour: r.hour,
      }));

      setLabId(data.labId);
      setLabName(data.labName || labIdInput.trim());
      setLabData(mapped);
      toast.success("✅ Lab timetable loaded successfully");
    } catch (e: any) {
      console.error(e);
      toast.error(`❌ ${e.message || "Something went wrong"}`);
    } finally {
      setLoading(false);
    }
  };

  const buildSlotMap = () => {
    const map: Record<string, Record<number, string>> = {};
    for (const r of labData) {
      if (!r.day || typeof r.day !== "string" || typeof r.hour !== "number") continue;
      const day = r.day.charAt(0).toUpperCase() + r.day.slice(1).toLowerCase();
      if (!map[day]) map[day] = {};
      map[day][r.hour] = `${r.subjectId} (${r.department}, ${r.year}-${r.section})`;
    }
    return map;
  };

  useEffect(() => {
    fetchLabs();
  }, []);

  const slotMap = buildSlotMap();

  return (
    <div className="staff-table-container">
      <ToastContainer position="top-right" autoClose={3000} hideProgressBar />

      <div className="staff-controls">
        {/* ✅ Dropdown Select instead of text input */}
        <select
          value={labIdInput}
          onChange={(e) => setLabIdInput(e.target.value)}
        >
          <option value="">🔽 Select Lab ID</option>
          {labs.map((lab) => (
            <option key={lab.labId} value={lab.labId}>
              {lab.labId} — {lab.labName}
            </option>
          ))}
        </select>
        <button onClick={handleFetchLabTimetable}>📥 View Lab Timetable</button>
      </div>

      {loading && <p>📡 Loading lab timetable…</p>}

      {labData.length > 0 && (
        <div className="staff-name-heading">
          <h3>
            🧪 Lab ID: <span style={{ color: "#2a6" }}>{labName}</span>
          </h3>
        </div>
      )}

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
                {periods.map((hour) => (
                  <td key={hour}>
                    {slotMap[day] && slotMap[day][hour]
                      ? slotMap[day][hour]
                      : "---"}
                  </td>
                ))}
              </tr>
            ))}
          </tbody>
        </table>
      </div>

      <div className="staff-button-group">
        <button className="staff-export-btn" onClick={handleExportPDF}>
          📤 Export to PDF
        </button>
      </div>
    </div>
  );
};

export default LabTable;
