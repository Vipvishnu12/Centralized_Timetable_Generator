import React, { useState } from "react";
import "../styles/Header.css";
// import logo from "../assets/nandha logo (1).svg";
import { FaUserCircle } from "react-icons/fa";

interface HeaderProps {
  email: string;
  onLogout: () => void;
}

const Header: React.FC<HeaderProps> = ({ email, onLogout }) => {
  const [showDropdown, setShowDropdown] = useState(false);

  const toggleDropdown = () => setShowDropdown(!showDropdown);

  return (
    <header className="header">
      <div className="title">Time Table</div>

      <div className="profile-wrapper" onClick={toggleDropdown}>
        <FaUserCircle className="profile-icon" />
        {showDropdown && (
          <div className="profile-dropdown">
            <div className="profile-email">{email}</div>
            <button
              onClick={() => {
                setShowDropdown(false);
                onLogout();
              }}
            >
              Logout
            </button>
          </div>
        )}
      </div>
    </header>
  );
};

export default Header;