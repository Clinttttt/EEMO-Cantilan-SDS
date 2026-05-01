import { useState, useEffect, KeyboardEvent } from 'react';
import { useNavigate, useSearchParams } from 'react-router-dom';
import { useAuth } from '@/context/AuthContext';
import styles from './Login.module.css';

export const Login = () => {
  const [searchParams] = useSearchParams();
  const navigate = useNavigate();
  const { login, isAuthenticated } = useAuth();

  const [username, setUsername] = useState('');
  const [password, setPassword] = useState('');
  const [showPassword, setShowPassword] = useState(false);
  const [isLoading, setIsLoading] = useState(false);
  const [errorMessage, setErrorMessage] = useState('');
  const [fieldErrors, setFieldErrors] = useState<Record<string, string>>({});
  const [failedAttempts, setFailedAttempts] = useState(0);
  const [isLocked, setIsLocked] = useState(false);
  const [lockoutRemaining, setLockoutRemaining] = useState('15 minutes');

  const isFromSetup = searchParams.get('setup') === 'true';

  const handleKeyDown = (e: KeyboardEvent<HTMLInputElement>) => {
    if (e.key === 'Enter' && !isLoading && !isLocked) {
      handleLogin();
    }
  };

  const handleLogin = async () => {
    setErrorMessage('');
    setFieldErrors({});
    setIsLoading(true);

    try {
      await login(username, password);
      navigate('/dashboard');
    } catch (error: any) {
      setFailedAttempts((prev) => prev + 1);
      setErrorMessage('Invalid username or password.');
      
      // Check if account should be locked (5 failed attempts)
      if (failedAttempts + 1 >= 5) {
        setIsLocked(true);
        setLockoutRemaining('15 minutes');
      }
    } finally {
      setIsLoading(false);
    }
  };

  const getFieldError = (fieldName: string) => fieldErrors[fieldName] || '';

  return (
    <div className="setup-root">
      {/* LEFT PANEL — Branding */}
      <div className="setup-left">
        <div className="setup-left-inner">
          <div className="setup-seal">
            <div className="seal-ring outer"></div>
            <div className="seal-ring inner"></div>
            <img src="/images/eemo-logov2.png" alt="EEMO Logo" />
          </div>
          <div className="setup-brand">
            <div className="setup-brand-label">Republic of the Philippines</div>
            <div className="setup-brand-city">Municipality of Cantilan</div>
            <div className="setup-brand-title">EEMO</div>
            <div className="setup-brand-sub">
              Economic Enterprise &<br />
              Management Office
            </div>
          </div>
          <div className="setup-left-footer">
            <div className="setup-system-name">Revenue Collection System</div>
            <div className="setup-version">Version 1.0</div>
          </div>
        </div>
      </div>

      {/* RIGHT PANEL — Login Form */}
      <div className="setup-right">
        <div className="setup-form-wrap">
          {/* Step Indicator — only visible when redirected from setup */}
          {isFromSetup && (
            <div className="setup-step-row">
              <div className={`setup-step ${styles.completed}`}>
                <span className="step-dot"></span>
                <span className="step-label">Administrator Setup</span>
              </div>
              <div className={`setup-step-line ${styles.setupStepLineDone}`}></div>
              <div className="setup-step active">
                <span className="step-dot"></span>
                <span className="step-label">Login</span>
              </div>
              <div className="setup-step-line"></div>
              <div className="setup-step">
                <span className="step-dot"></span>
                <span className="step-label">Dashboard</span>
              </div>
            </div>
          )}

          {/* Header */}
          <div className={styles.setupFormHeader}>
            <div className={styles.setupFormEyebrow}>
              {isFromSetup ? 'Step 2 of 3' : 'Administrator Access'}
            </div>
            <div className={styles.setupFormTitle}>
              {isFromSetup ? 'Sign In to Continue' : 'Sign In'}
            </div>
            <div className={styles.setupFormDesc}>
              {isFromSetup
                ? 'Your administrator account has been created. Sign in to access the EEMO Revenue Collection System.'
                : 'Enter your credentials to access the EEMO Revenue Collection System. Authorized personnel only.'}
            </div>
          </div>

          {/* Security notice — only on normal login */}
          {!isFromSetup && (
            <div className={`${styles.setupNotice} ${styles.loginNotice}`}>
              <svg viewBox="0 0 24 24" fill="none" stroke="currentColor" strokeWidth="1.8">
                <rect x="3" y="11" width="18" height="11" rx="2" ry="2" />
                <path d="M7 11V7a5 5 0 0110 0v4" />
              </svg>
              <span>
                This portal is restricted to authorized EEMO personnel only. Unauthorized access is prohibited.
              </span>
            </div>
          )}

          {/* Login Form */}
          <div className={styles.setupForm}>
            {/* Username */}
            <div className={styles.formGroup}>
              <label className={styles.formLabel}>
                Username <span className={styles.formRequired}>*</span>
              </label>
              <div
                className={`${styles.formInputWrap} ${
                  getFieldError('Username') ? styles.formInputError : ''
                }`}
              >
                <svg
                  className={styles.formInputIcon}
                  viewBox="0 0 24 24"
                  fill="none"
                  stroke="currentColor"
                  strokeWidth="1.8"
                >
                  <path d="M20 21v-2a4 4 0 00-4-4H8a4 4 0 00-4 4v2" />
                  <circle cx="12" cy="7" r="4" />
                </svg>
                <input
                  type="text"
                  className={styles.formInput}
                  placeholder="Enter your username"
                  autoComplete="username"
                  value={username}
                  onChange={(e) => setUsername(e.target.value)}
                  onKeyDown={handleKeyDown}
                  disabled={isLocked}
                />
              </div>
              {getFieldError('Username') && (
                <span className="form-error">{getFieldError('Username')}</span>
              )}
            </div>

            {/* Password */}
            <div className={styles.formGroup}>
              <label className={styles.formLabel}>
                Password <span className={styles.formRequired}>*</span>
              </label>
              <div
                className={`${styles.formInputWrap} ${
                  getFieldError('Password') ? styles.formInputError : ''
                }`}
              >
                <svg
                  className={styles.formInputIcon}
                  viewBox="0 0 24 24"
                  fill="none"
                  stroke="currentColor"
                  strokeWidth="1.8"
                >
                  <rect x="3" y="11" width="18" height="11" rx="2" ry="2" />
                  <path d="M7 11V7a5 5 0 0110 0v4" />
                </svg>
                <input
                  type={showPassword ? 'text' : 'password'}
                  className={`${styles.formInput} ${styles.formInputPassword}`}
                  placeholder="Enter your password"
                  autoComplete="current-password"
                  value={password}
                  onChange={(e) => setPassword(e.target.value)}
                  onKeyDown={handleKeyDown}
                  disabled={isLocked}
                />
                <button
                  type="button"
                  className={styles.formEyeBtn}
                  onClick={() => setShowPassword(!showPassword)}
                  tabIndex={-1}
                >
                  {showPassword ? (
                    <svg viewBox="0 0 24 24" fill="none" stroke="currentColor" strokeWidth="1.8">
                      <path d="M17.94 17.94A10.07 10.07 0 0112 20c-7 0-11-8-11-8a18.45 18.45 0 015.06-5.94M9.9 4.24A9.12 9.12 0 0112 4c7 0 11 8 11 8a18.5 18.5 0 01-2.16 3.19m-6.72-1.07a3 3 0 11-4.24-4.24" />
                      <line x1="1" y1="1" x2="23" y2="23" />
                    </svg>
                  ) : (
                    <svg viewBox="0 0 24 24" fill="none" stroke="currentColor" strokeWidth="1.8">
                      <path d="M1 12s4-8 11-8 11 8 11 8-4 8-11 8-11-8-11-8z" />
                      <circle cx="12" cy="12" r="3" />
                    </svg>
                  )}
                </button>
              </div>
              {getFieldError('Password') && (
                <span className="form-error">{getFieldError('Password')}</span>
              )}
            </div>

            {/* Failed attempts warning */}
            {failedAttempts > 0 && failedAttempts < 5 && !isLocked && (
              <div className={styles.loginAttemptsWarn}>
                <svg viewBox="0 0 24 24" fill="none" stroke="currentColor" strokeWidth="1.8">
                  <path d="M10.29 3.86L1.82 18a2 2 0 001.71 3h16.94a2 2 0 001.71-3L13.71 3.86a2 2 0 00-3.42 0z" />
                  <line x1="12" y1="9" x2="12" y2="13" />
                  <line x1="12" y1="17" x2="12.01" y2="17" />
                </svg>
                <span>
                  Incorrect credentials.{' '}
                  <strong>
                    {5 - failedAttempts} attempt{5 - failedAttempts === 1 ? '' : 's'}
                  </strong>{' '}
                  remaining before account is temporarily locked.
                </span>
              </div>
            )}

            {/* Locked banner */}
            {isLocked && (
              <div className={styles.loginLockedBanner}>
                <div className={styles.loginLockedIcon}>
                  <svg viewBox="0 0 24 24" fill="none" stroke="currentColor" strokeWidth="1.8">
                    <rect x="3" y="11" width="18" height="11" rx="2" ry="2" />
                    <path d="M7 11V7a5 5 0 0110 0v4" />
                  </svg>
                </div>
                <div>
                  <div className={styles.loginLockedTitle}>Account Temporarily Locked</div>
                  <div className={styles.loginLockedSub}>
                    Too many failed attempts. Please try again in <strong>{lockoutRemaining}</strong>.
                  </div>
                </div>
              </div>
            )}

            {/* General error */}
            {errorMessage && !isLocked && (
              <div className={styles.setupError}>
                <svg viewBox="0 0 24 24" fill="none" stroke="currentColor" strokeWidth="1.8">
                  <circle cx="12" cy="12" r="10" />
                  <line x1="15" y1="9" x2="9" y2="15" />
                  <line x1="9" y1="9" x2="15" y2="15" />
                </svg>
                {errorMessage}
              </div>
            )}

            {/* Submit button */}
            <button
              className={`${styles.setupSubmit} ${isLoading ? styles.setupSubmitLoading : ''} ${
                isLocked ? styles.setupSubmitLocked : ''
              }`}
              onClick={handleLogin}
              disabled={isLoading || isLocked}
            >
              {isLoading ? (
                <>
                  <span className={styles.setupSpinner}></span>
                  <span>Verifying credentials...</span>
                </>
              ) : isLocked ? (
                <>
                  <svg viewBox="0 0 24 24" fill="none" stroke="currentColor" strokeWidth="2">
                    <rect x="3" y="11" width="18" height="11" rx="2" ry="2" />
                    <path d="M7 11V7a5 5 0 0110 0v4" />
                  </svg>
                  <span>Account Locked — {lockoutRemaining}</span>
                </>
              ) : (
                <>
                  <svg viewBox="0 0 24 24" fill="none" stroke="currentColor" strokeWidth="2">
                    <path d="M15 3h4a2 2 0 012 2v14a2 2 0 01-2 2h-4" />
                    <polyline points="10 17 15 12 10 7" />
                    <line x1="15" y1="12" x2="3" y2="12" />
                  </svg>
                  <span>Sign In to System</span>
                </>
              )}
            </button>
          </div>

          {/* Footer */}
          <div className={styles.setupFormFooter}>
            Republic of the Philippines &middot; Municipality of Cantilan &middot; EEMO
          </div>
        </div>
      </div>
    </div>
  );
};
