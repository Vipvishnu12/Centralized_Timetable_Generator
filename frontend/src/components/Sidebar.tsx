import React, { useState, useEffect } from "react";
import { FiChevronDown, FiChevronUp } from "react-icons/fi";
import "../styles/Sidebar.css";

interface SidebarProps {
  setActivePage: (page: string) => void;
  role: string;
}

const Sidebar: React.FC<SidebarProps> = ({ setActivePage, role }) => {
  const [activeMenu, setActiveMenu] = useState<string | null>(null);
  const [pendingLabCount, setPendingLabCount] = useState<number>(0);

  const isAdmin = role?.toUpperCase() === "ADMIN";

  const toggleMenu = (menuName: string) => {
    setActiveMenu((prev) => (prev === menuName ? null : menuName));
  };

  const handleMenuClick = (page: string) => {
    setActivePage(page);
    setActiveMenu(null);
  };

  // Fetch pending lab requests count
  useEffect(() => {
    const fetchPendingLabCount = async () => {
      try {
        const res = await fetch("https://localhost:7244/api/Lab/pendingLabRequestsCount");
        if (res.ok) {
          const data = await res.json();
          console.log("Pending lab count response:", data);
          setPendingLabCount(data.pendingLabRequestsCount || 0);
        } else {
          console.error("Failed to fetch pending lab requests count. Status:", res.status);
          const errorText = await res.text();
          console.error("Error response:", errorText);
        }
      } catch (error) {
        console.error("Error fetching pending lab requests count:", error);
        // Set default value on error
        setPendingLabCount(0);
      }
    };
console.log(pendingLabCount);
    fetchPendingLabCount();
    const intervalId = setInterval(fetchPendingLabCount, 60000);
    return () => clearInterval(intervalId);
  }, []);

  return (
    <div className="sidebar">
      {/* DEPARTMENT */}
      <div className="menu-item" onClick={() => toggleMenu("department")}>
        <span>DEPARTMENT</span>
        {activeMenu === "department" ? <FiChevronUp /> : <FiChevronDown />}
      </div>
      {activeMenu === "department" && (
        <div className="submenu">
          {isAdmin && (
            <div
              className="submenu-item"
              onClick={() => handleMenuClick("admin")}
            >
              CREATE-DEPARTMENT
            </div>
          )}
          {!isAdmin && (
            <>
              <div
                className="submenu-item"
                onClick={() => handleMenuClick("Department")}
              >
                ADD STAFF
              </div>
              <div
                className="submenu-item"
                onClick={() => handleMenuClick("viewstaff")}
              >
                SHOW STAFF
              </div>
            </>
          )}
        </div>
      )}

      {/* USER ONLY MENUS */}
      {!isAdmin && (
        <>
          {/* SUBJECT */}
          <div className="menu-item" onClick={() => toggleMenu("subject")}>
            <span>SUBJECT</span>
            {activeMenu === "subject" ? <FiChevronUp /> : <FiChevronDown />}
          </div>
          {activeMenu === "subject" && (
            <div className="submenu">
              <div
                className="submenu-item"
                onClick={() => handleMenuClick("subject")}
              >
                ADD SUBJECT
              </div>
              <div
                className="submenu-item"
                onClick={() => handleMenuClick("viewSubject")}
              >
                VIEW SUBJECT
              </div>
            </div>
          )}

          {/* TIMETABLE */}
          <div className="menu-item" onClick={() => handleMenuClick("Table")}>
            TIMETABLE
          </div>

          {/* REQUEST */}
          <div className="menu-item" onClick={() => toggleMenu("request")}>
            <span>REQUEST</span>
            {activeMenu === "request" ? <FiChevronUp /> : <FiChevronDown />}
          </div>
          {activeMenu === "request" && (
            <div className="submenu">
              <div
                className="submenu-item"
                onClick={() => handleMenuClick("pending")}
              >
                <span>SEND</span>
                {(pendingLabCount > 0 || true) && (
                  <span className="red-dot">{pendingLabCount || 1}</span>
                )}
              </div>
              <div
                className="submenu-item"
                onClick={() => handleMenuClick("received")}
              >
                RECEIVED
              </div>
              {/* LAB RECEIVED */}
              <div
                className="submenu-item lab-received"
                onClick={() => handleMenuClick("labReceived")}
              >
                <span>LAB RECEIVED</span>
              </div>
            </div>
          )}
        </>
      )}

      {/* VIEW TABLE */}

      <div className="menu-item" onClick={() => toggleMenu("viewTable")}>
        <div>
          <span>VIEW TABLE</span>
        </div>
        <div>
          {activeMenu === "viewTable" ? <FiChevronUp /> : <FiChevronDown />}
        </div>
      </div>
      {activeMenu === "viewTable" && (
        <div className="submenu">
          <div
            className="submenu-item"
            onClick={() => handleMenuClick("ViewTable")}
          >
            CLASS TABLE
          </div>
          <div
            className="submenu-item"
            onClick={() => handleMenuClick("Tablestaff")}
          >
            STAFF TABLE
          </div>
          <div
            className="submenu-item"
            onClick={() => handleMenuClick("viewOverallTable")}
          >
            OVERALL TABLE
          </div>
          <div
            className="submenu-item"
            onClick={() => handleMenuClick("LabTable")}
          >
            LAB TABLE
          </div>
        </div>
      )}

      {/* LAB MANAGEMENT */}
      <div className="menu-item" onClick={() => toggleMenu("lab")}>
        <span>LAB</span>
        {activeMenu === "lab" ? <FiChevronUp /> : <FiChevronDown />}
      </div>
      {activeMenu === "lab" && (
        <div className="submenu">
          {isAdmin && (
            <div
              className="submenu-item"
              onClick={() => handleMenuClick("labCreation")}
            >
              LAB CREATION
            </div>
          )}
          <div
            className="submenu-item"
            onClick={() => handleMenuClick("viewLab")}
          >
            VIEW LAB
          </div>
        </div>
      )}
    </div>
  );
};

export default Sidebar;