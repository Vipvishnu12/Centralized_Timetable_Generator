import React, { useState } from 'react';
import { ToastContainer, toast } from 'react-toastify';
import 'react-toastify/dist/ReactToastify.css';
import '../styles/Login.css';
import NEC from '../assets/image.png';

interface LoginProps {
  onLoginSuccess: (userEmail: string, userRole: string) => void;
}

const Login: React.FC<LoginProps> = ({ onLoginSuccess }) => {
  const [username, setUsername] = useState('');
  const [password, setPassword] = useState('');

  const handleSubmit = async (e: React.FormEvent) => {
    e.preventDefault();

    if (!username || !password) {
      toast.error('❌ Please enter both username and password.');
      return;
    }
  
    try {
      const response = await fetch('https://localhost:7244/api/Login/verify', {
        method: 'POST',
        headers: {
          'Content-Type': 'application/json',
        },
        body: JSON.stringify({ username, password }),
      });

      if (response.ok) {
        const data = await response.json();
        const loggedInUsername = data.username || username;
        localStorage.setItem('loggedUser', loggedInUsername);

        toast.success('✅ Login successful!');
        onLoginSuccess(loggedInUsername, data.role);
      } else if (response.status === 401) {
        toast.error('❌ Incorrect username or password.');
      } else {
        toast.error('⚠️ Login failed. Please try again.');
      }
    } catch (error) {
      toast.error('🚫 Server error. Please try again later.');
      console.error('Login error:', error);
    }
  };

  return (
    <div className="login-page">
      {/* ✅ Toast Container with right side and no auto-close */}
      <ToastContainer
        position="top-right"
        hideProgressBar={false}
        closeOnClick
        pauseOnHover
        draggable
        theme="light"
      />

      <div className="login-left">
        <img src={NEC} alt="Nandha Engineering College Logo" className="logo-image" />
        <p className="logo-text">CENTRALIZED TIMETABLE GENERATOR</p>
      </div>

      <div className="login-right">
        <form onSubmit={handleSubmit} className="login-form">
          <h2 className="login-label">Login Page</h2>
          <div className="user-name">
            <label>Username :</label>
            <input
              type="text"
              placeholder="Enter username"
              value={username}
              onChange={(e) => setUsername(e.target.value.toUpperCase())}
              required
            />
          </div>
          <div className="pass-word">
            <label>Password :</label>
            <input
              type="password"
              placeholder="Enter password"
              value={password}
              onChange={(e) => setPassword(e.target.value)}
              required
            />
          </div>
          <button type="submit">LOGIN</button>
        </form>
      </div>
    </div>
  );
};

export default Login;
