import { useState } from 'react';
import { useNavigate } from 'react-router-dom';
import { useCreateFirstAdmin } from '@/hooks/mutations/useAuthMutations';
import type { CreateFirstAdminCommand } from '@/types/dto';
import styles from './AccountSetup.module.css';

interface SetupForm {
  fullName: string;
  username: string;
  email: string;
  password: string;
  confirmPassword: string;
}

export const AccountSetup = () => {
  const navigate = useNavigate();
  const createFirstAdmin = useCreateFirstAdmin();

  const [form, setForm] = useState<SetupForm>({
    fullName: '',
    username: '',
    email: '',
    password: '',
    confirmPassword: '',
  });

  const [fieldErrors, setFieldErrors] = useState<Record<string, string>>({});
  const [showPassword, setShowPassword] = useState(false);
  const [errorMessage, setErrorMessage] = useState('');

  const getPasswordStrength = (pw: string): number => {
    let score = 0;
    if (pw.length >= 8) score++;
    if (/[A-Z]/.test(pw)) score++;
    if (/\d/.test(pw)) score++;
    if (/[^A-Za-z0-9]/.test(pw)) score++;
    return score;
  };

  const getStrengthClass = (strength: number): string => {
    switch (strength) {
      case 1:
        return 'weak';
      case 2:
        return 'fair';
      case 3:
        return 'good';
      case 4:
        return 'strong';
      default:
        return '';
    }
  };

  const getStrengthLabel = (strength: number): string => {
    switch (strength) {
      case 1:
        return 'Weak';
      case 2:
        return 'Fair';
      case 3:
        return 'Good';
      case 4:
        return 'Strong';
      default:
        return '';
    }
  };

  const handleSubmit = async () => {
    setErrorMessage('');
    setFieldErrors({});

    // Client-side validation
    if (form.password !== form.confirmPassword) {
      setFieldErrors({ ConfirmPassword: 'Passwords do not match.' });
      return;
    }

    try {
      const command: CreateFirstAdminCommand = {
        fullName: form.fullName,
        username: form.username,
        email: form.email,
        password: form.password,
      };

      await createFirstAdmin.mutateAsync(command);
      navigate('/login?setup=true');
    } catch (error: any) {
      if (error.response?.data?.errors) {
        const backendErrors: Record<string, string> = {};
        Object.entries(error.response.data.errors).forEach(([key, value]) => {
          backendErrors[key] = (value as string[])[0];
        });
        setFieldErrors(backendErrors);
      } else {
        setErrorMessage(error.response?.data?.error || 'Failed to create administrator account.');
      }
    }
  };

  const getFieldError = (fieldName: string) => fieldErrors[fieldName] || '';

  const passwordStrength = form.password ? getPasswordStrength(form.password) : 0;

  return (
    <div className="setup-root">
      {/* Left Panel — Branding */}
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
            <div className="setup-version">Version 1.0 — Initial Setup</div>
          </div>
        </div>
      </div>

      {/* Right Panel — Form */}
      <div className="setup-right">
        <div className="setup-form-wrap">
          {/* Step indicator */}
          <div className="setup-step-row">
            <div className="setup-step active">
              <span className="step-dot"></span>
              <span className="step-label">Administrator Setup</span>
            </div>
            <div className="setup-step-line"></div>
            <div className="setup-step">
              <span className="step-dot"></span>
              <span className="step-label">Login</span>
            </div>
            <div className="setup-step-line"></div>
            <div className="setup-step">
              <span className="step-dot"></span>
              <span className="step-label">Dashboard</span>
            </div>
          </div>

          {/* Header */}
          <div className={styles.setupFormHeader}>
            <div className={styles.setupFormEyebrow}>First Time Setup</div>
            <div className={styles.setupFormTitle}>Create Administrator Account</div>
            <div className={styles.setupFormDesc}>
              This account will have full access to the EEMO Revenue Collection System. Please keep your credentials
              secure.
            </div>
          </div>

          {/* Notice Banner */}
          <div className={styles.setupNotice}>
            <svg viewBox="0 0 24 24" fill="none" stroke="currentColor" strokeWidth="1.8">
              <circle cx="12" cy="12" r="10" />
              <line x1="12" y1="8" x2="12" y2="12" />
              <line x1="12" y1="16" x2="12.01" y2="16" />
            </svg>
            <span>This setup page will be permanently disabled once the administrator account is created.</span>
          </div>

          {/* Form */}
          <div className={styles.setupForm}>
            {/* Full Name */}
            <div className={styles.formGroup}>
              <label className={styles.formLabel}>
                Full Name <span className={styles.formRequired}>*</span>
              </label>
              <div
                className={`${styles.formInputWrap} ${
                  getFieldError('FullName') ? styles.formInputError : ''
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
                  placeholder="e.g. Juan D. Dela Cruz"
                  value={form.fullName}
                  onChange={(e) => setForm({ ...form, fullName: e.target.value })}
                />
              </div>
              {getFieldError('FullName') && <span className="form-error">{getFieldError('FullName')}</span>}
            </div>

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
                  placeholder="e.g. admin.eemo"
                  value={form.username}
                  onChange={(e) => setForm({ ...form, username: e.target.value })}
                />
              </div>
              {getFieldError('Username') ? (
                <span className="form-error">{getFieldError('Username')}</span>
              ) : (
                <span className={styles.formHint}>Used to log in. No spaces allowed.</span>
              )}
            </div>

            {/* Email */}
            <div className={styles.formGroup}>
              <label className={styles.formLabel}>
                Email Address <span className={styles.formRequired}>*</span>
              </label>
              <div
                className={`${styles.formInputWrap} ${
                  getFieldError('Email') ? styles.formInputError : ''
                }`}
              >
                <svg
                  className={styles.formInputIcon}
                  viewBox="0 0 24 24"
                  fill="none"
                  stroke="currentColor"
                  strokeWidth="1.8"
                >
                  <path d="M4 4h16c1.1 0 2 .9 2 2v12c0 1.1-.9 2-2 2H4c-1.1 0-2-.9-2-2V6c0-1.1.9-2 2-2z" />
                  <polyline points="22,6 12,13 2,6" />
                </svg>
                <input
                  type="email"
                  className={styles.formInput}
                  placeholder="e.g. eemo@cantilan.gov.ph"
                  value={form.email}
                  onChange={(e) => setForm({ ...form, email: e.target.value })}
                />
              </div>
              {getFieldError('Email') && <span className="form-error">{getFieldError('Email')}</span>}
            </div>

            {/* Two col — Password */}
            <div className={styles.formRow}>
              <div className={styles.formGroup}>
                <label className={styles.formLabel}>
                  Password <span className={styles.formRequired}>*</span>
                </label>
                <div className={styles.formInputWrapPassword}>
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
                      placeholder="Min. 8 characters"
                      value={form.password}
                      onChange={(e) => setForm({ ...form, password: e.target.value })}
                    />
                    <button
                      type="button"
                      className={styles.formEyeBtn}
                      onClick={() => setShowPassword(!showPassword)}
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
                </div>
                {getFieldError('Password') && <span className="form-error">{getFieldError('Password')}</span>}
              </div>

              <div className={styles.formGroup}>
                <label className={styles.formLabel}>
                  Confirm Password <span className={styles.formRequired}>*</span>
                </label>
                <div
                  className={`${styles.formInputWrap} ${
                    getFieldError('ConfirmPassword') ? styles.formInputError : ''
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
                    className={styles.formInput}
                    placeholder="Re-enter password"
                    value={form.confirmPassword}
                    onChange={(e) => setForm({ ...form, confirmPassword: e.target.value })}
                  />
                </div>
                {getFieldError('ConfirmPassword') && (
                  <span className="form-error">{getFieldError('ConfirmPassword')}</span>
                )}
              </div>
            </div>

            {/* Password strength */}
            {form.password && (
              <div className={styles.pwStrength}>
                <div className={styles.pwStrengthBars}>
                  <div
                    className={`${styles.pwBar} ${
                      passwordStrength >= 1 ? `${styles.pwBarFill} ${styles[`pw${getStrengthClass(passwordStrength).charAt(0).toUpperCase() + getStrengthClass(passwordStrength).slice(1)}`]}` : ''
                    }`}
                  ></div>
                  <div
                    className={`${styles.pwBar} ${
                      passwordStrength >= 2 ? `${styles.pwBarFill} ${styles[`pw${getStrengthClass(passwordStrength).charAt(0).toUpperCase() + getStrengthClass(passwordStrength).slice(1)}`]}` : ''
                    }`}
                  ></div>
                  <div
                    className={`${styles.pwBar} ${
                      passwordStrength >= 3 ? `${styles.pwBarFill} ${styles[`pw${getStrengthClass(passwordStrength).charAt(0).toUpperCase() + getStrengthClass(passwordStrength).slice(1)}`]}` : ''
                    }`}
                  ></div>
                  <div
                    className={`${styles.pwBar} ${
                      passwordStrength >= 4 ? `${styles.pwBarFill} ${styles[`pw${getStrengthClass(passwordStrength).charAt(0).toUpperCase() + getStrengthClass(passwordStrength).slice(1)}`]}` : ''
                    }`}
                  ></div>
                </div>
                <span className={`${styles.pwStrengthLabel} ${styles[`pw${getStrengthClass(passwordStrength).charAt(0).toUpperCase() + getStrengthClass(passwordStrength).slice(1)}`]}`}>
                  {getStrengthLabel(passwordStrength)}
                </span>
              </div>
            )}

            {/* Error */}
            {errorMessage && (
              <div className={styles.setupError}>
                <svg viewBox="0 0 24 24" fill="none" stroke="currentColor" strokeWidth="1.8">
                  <circle cx="12" cy="12" r="10" />
                  <line x1="15" y1="9" x2="9" y2="15" />
                  <line x1="9" y1="9" x2="15" y2="15" />
                </svg>
                {errorMessage}
              </div>
            )}

            {/* Submit */}
            <button
              className={`${styles.setupSubmit} ${createFirstAdmin.isPending ? styles.setupSubmitLoading : ''}`}
              onClick={handleSubmit}
              disabled={createFirstAdmin.isPending}
            >
              {createFirstAdmin.isPending ? (
                <>
                  <span className={styles.setupSpinner}></span>
                  <span>Creating account...</span>
                </>
              ) : (
                <>
                  <svg viewBox="0 0 24 24" fill="none" stroke="currentColor" strokeWidth="2">
                    <polyline points="20 6 9 17 4 12" />
                  </svg>
                  <span>Create Administrator Account</span>
                </>
              )}
            </button>
          </div>

          <div className={styles.setupFormFooter}>
            Republic of the Philippines · Municipality of Cantilan · EEMO
          </div>
        </div>
      </div>
    </div>
  );
};
