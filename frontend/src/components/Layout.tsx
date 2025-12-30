// src/components/Layout.tsx
import React from 'react';
import Header from './Header';
import Sidebar from './Sidebar';
import '../styles/App.css';

interface LayoutProps {
  email: string;
  onLogout: () => void;
  onPageChange: (page: string) => void;
  role: string;
  children: React.ReactNode;
}
 
const Layout: React.FC<LayoutProps> = ({ email, onLogout, onPageChange, role, children }) => {
  return (
    <div className="app-container">
      <Header email={email} onLogout={onLogout} />
      <div className="app-body">
        <Sidebar setActivePage={onPageChange} role={role} />
        <div className="main-content">{children}</div>
      </div>
    </div>
  );
};

export default Layout;
