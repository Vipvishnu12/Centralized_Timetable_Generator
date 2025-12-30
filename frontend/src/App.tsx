import React, { useState, useEffect } from 'react';
import { BrowserRouter as Router, Routes, Route } from 'react-router-dom';

import Header from './components/Header';
import Sidebar from './components/Sidebar';
import '../src/styles/App.css';

import Admin from './components/admin';
import ViewTable from './components/ViewTable';
import Department from './components/Department';
import Staff from './components/Staff';
import Subject from './components/subject';
import Table from './components/Table';
import Login from './components/login';
import ViewSubject from './components/ViewSubject';
import ViewStaff from './components/viewstaff';
import Pending from './components/pending';
import Received from './components/Recived';
import Tablestaff from './components/tablestaff';
import OverallStaff from './components/overall_timetable';
import LabCreation from './components/lab';
import ViewLab from './components/viewlab';
import LabTable from './components/view_Lab_table';
import LabReceived from './components/labrecived'; // Assuming this is the new component for lab received

const ApprovalPage: React.FC = () => (
  <div className="approval-page">
    <h2>Request Submitted</h2>
    <p>Your assignment was saved and is waiting for approval from the HOD.</p>
  </div>
);

const App: React.FC = () => {
  const [email, setEmail] = useState('');
  const [role, setRole] = useState('');
  const [activePage, setActivePage] = useState('');
  const [totalStaff, setTotalStaff] = useState<number>(0);
  const [subjectCount, setSubjectCount] = useState<number>(0);
  const [showStaffBelow, setShowStaffBelow] = useState(false);
  const [departmentData, setDepartmentData] = useState({
    department: '',
    departmentName: '',
    block: '',
  });

  useEffect(() => {
    const storedRole = localStorage.getItem('role');
    if (storedRole) setRole(storedRole);

    // Set default page based on role
    if (storedRole === 'ADMIN') {
      setActivePage('ViewTable'); // CREATE-DEPARTMENT for ADMIN
    } else if (storedRole) {
      setActivePage('ViewTable'); // ADD STAFF for USER
    }
  }, []);

  const handleLoginSuccess = (userEmail: string, userRole: string) => {
    setEmail(userEmail);
    setRole(userRole);

    // Set default page after login based on role
    if (userRole === 'ADMIN') {
      setActivePage('ViewTable'); // CREATE-DEPARTMENT for ADMIN
    } else {
      setActivePage('ViewTable'); // ADD STAFF for USER
    }

    localStorage.setItem('role', userRole);
  };

  const handleLogout = () => {
    setEmail('');
    setRole('');
    setActivePage('');
    localStorage.clear();
  };

  const renderContent = () => {
    if (role === 'ADMIN') {
      switch (activePage) {
        case 'Department':
          return (
            <Department
              totalStaff={totalStaff}
              setTotalStaff={setTotalStaff}
              setDepartmentData={setDepartmentData}
              onShowStaff={() => setShowStaffBelow(true)}
            />
          );
        case 'admin':
          return <Admin />;
        case 'ViewTable':
          return <ViewTable />;
        case 'LabTable':
          return <LabTable />;
        case 'labCreation':
          return (
            <LabCreation
              onLabSave={(data) => {
                console.log('Lab created:', data);
              }}
            />
          );
        case 'viewLab':
          return <ViewLab />;
        default:
          return <div className="fallback-message">Select a page from the sidebar.</div>;
      }
    }
    // USER: original content
    switch (activePage) {
      case 'Department':
        return (
          <>
            <Department
              totalStaff={totalStaff}
              setTotalStaff={setTotalStaff}
              setDepartmentData={setDepartmentData}
              onShowStaff={() => setShowStaffBelow(true)}
            />
            {showStaffBelow && (
              <Staff totalStaff={totalStaff} departmentData={departmentData} />
            )}
          </>
        );
      case 'Staff':
        return <Staff totalStaff={totalStaff} departmentData={departmentData} />;
      case 'subject':
        return (
          <Subject
            subjectCount={subjectCount}
            setSubjectCount={setSubjectCount}
            setActivePage={setActivePage}
          />
        );
      case 'viewSubject':
        return <ViewSubject />;
      case 'Table':
        return <Table />;
      case 'viewstaff':
        return <ViewStaff />;
      case 'ViewTable':
        return <ViewTable />;
      case 'received':
        return <Received />;
        case  'labReceived':
        return <LabReceived />;
      case 'pending':
        return <Pending />;
      case 'admin':
        return <Admin />;
      case 'Tablestaff':
        return <Tablestaff />;
      case 'viewOverallTable':
        return <OverallStaff />;
      case 'LabTable':
        return <LabTable />;
      case 'labCreation':
        return (
          <LabCreation
            onLabSave={(data) => {
              console.log('Lab created:', data);
            }}
          />
        );
      case 'viewLab':
        return <ViewLab />;
      default:
        return <div className="fallback-message">Select a page from the sidebar.</div>;
    }
  };

  if (!email) {
    return <Login onLoginSuccess={handleLoginSuccess} />;
  }

  return (
    <Router>
      <Routes>
        <Route
          path="/"
          element={
            <div className="app-container">
              <Header email={email} onLogout={handleLogout} />
              <div className="app-body">
                <Sidebar
                  setActivePage={(page) => {
                    setActivePage(page);
                    setShowStaffBelow(false);
                  }}
                  role={role}
                />
                <div className="main-content">{renderContent()}</div>
              </div>
            </div>
          }
        />
        <Route path="/approval" element={<ApprovalPage />} />
      </Routes>
    </Router>
  );
};

export default App;