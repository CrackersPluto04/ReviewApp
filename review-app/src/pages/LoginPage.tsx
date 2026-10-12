import { Alert, Button, Link, Stack, TextField, Typography } from "@mui/material";
import LoginIcon from '@mui/icons-material/Login';
import HowToRegIcon from '@mui/icons-material/HowToReg';
import { useEffect, useState } from "preact/hooks";
import { authService } from "../services/AuthService";
import { useLocation, useNavigate } from "react-router-dom";
import { AuthUser, useAuth } from "../context/AuthContext";

const RESEND_COOLDOWN_SECONDS = 60;

export function LoginPage() {
    const navigate = useNavigate();
    const location = useLocation();
    const { setUser } = useAuth();

    const [username, setUsername] = useState('');
    const [email, setEmail] = useState('');
    const [password, setPassword] = useState('');
    const [confirmPassword, setConfirmPassword] = useState('');

    const [register, setRegister] = useState(false);
    const [message, setMessage] = useState('');
    const [success, setSuccess] = useState(true);

    // Second step of a 2FA login: set when the password was right but an emailed code is needed too
    const [challengeToken, setChallengeToken] = useState('');
    const [code, setCode] = useState('');
    const [submitting, setSubmitting] = useState(false);
    const [resendCooldown, setResendCooldown] = useState(0);

    useEffect(() => {
        if (resendCooldown <= 0) return;
        const timer = setTimeout(() => setResendCooldown(prev => prev - 1), 1000);
        return () => clearTimeout(timer);
    }, [resendCooldown]);

    const finishLogin = (loggedInUser: AuthUser) => {
        setUser(loggedInUser);

        const returnTo = location.state?.returnTo;
        if (returnTo) {
            const fullReturnPath = returnTo.pathname + (returnTo.search || '');
            navigate(fullReturnPath, { state: returnTo.state });
        } else {
            navigate('/home');
        }
    };

    const handleAuth = async (e: Event) => {
        e.preventDefault();

        if (register) {
            const result = await authService.register(username, email, password, confirmPassword);

            setSuccess(result.success);
            setMessage(result.message || '');
        } else {
            setSubmitting(true);
            const result = await authService.login(email, password);
            setSubmitting(false);

            setSuccess(result.success);
            setMessage(result.message || '');

            if (result.success && result.data.twoFactorRequired) {
                setChallengeToken(result.data.challengeToken);
                setPassword('');
                setCode('');
                setMessage(result.data.message);
                setResendCooldown(RESEND_COOLDOWN_SECONDS);
            } else if (result.success) {
                finishLogin(result.data.user);
            }
        }
    }

    const handleVerifyCode = async (e: Event) => {
        e.preventDefault();
        if (!/^\d{6}$/.test(code.trim())) {
            setSuccess(false);
            setMessage('Please enter the 6-digit code from the email.');
            return;
        }

        setSubmitting(true);
        const result = await authService.loginTwoFactor(challengeToken, code);
        setSubmitting(false);

        if (result.success) {
            finishLogin(result.data.user);
        } else {
            setSuccess(false);
            setMessage(result.message || '');
            setCode('');
        }
    };

    const handleResendCode = async () => {
        const result = await authService.resendTwoFactorCode(challengeToken);
        setSuccess(result.success);
        setMessage(result.message || '');
        if (result.success) setResendCooldown(RESEND_COOLDOWN_SECONDS);
    };

    const handleBackToLogin = () => {
        setChallengeToken('');
        setCode('');
        setMessage('');
        setSuccess(true);
    };

    if (challengeToken)
        return <Stack component="form" onSubmit={handleVerifyCode} spacing={2} alignItems="center">
            <Typography variant="h4" component="h1" gutterBottom>
                Two-factor authentication
            </Typography>

            {message && <Alert severity={success ? "info" : "error"}>{message}</Alert>}

            <Typography variant="body2" color="text.secondary" textAlign="center" sx={{ maxWidth: 360 }}>
                Enter the 6-digit code we sent to your email address. It expires in 10 minutes. Check your spam folder if you can't find it.
            </Typography>

            <TextField required autoFocus label="Code" variant="outlined"
                value={code}
                onChange={(e) => setCode(e.currentTarget.value.replace(/\D/g, '').slice(0, 6))}
                slotProps={{ htmlInput: { inputMode: 'numeric', autoComplete: 'one-time-code', maxLength: 6, style: { letterSpacing: 6, textAlign: 'center' } } }}
            />

            <Button type="submit" variant="contained" startIcon={<LoginIcon />} disabled={submitting || code.length !== 6}>
                Verify and log in
            </Button>

            <Stack direction="row" spacing={2}>
                <Button type="button" onClick={handleResendCode} disabled={resendCooldown > 0}>
                    {resendCooldown > 0 ? `Resend code (${resendCooldown}s)` : 'Resend code'}
                </Button>
                <Button type="button" color="inherit" onClick={handleBackToLogin}>
                    Back to login
                </Button>
            </Stack>
        </Stack>;

    return <Stack component="form" onSubmit={handleAuth} spacing={2} alignItems="center">
        <Typography variant="h4" component="h1" gutterBottom>
            {register ? "Register Account" : "Login to your account"}
        </Typography>

        {message && <Alert severity={success ? "success" : "error"}>{message}</Alert>}

        {register &&
            <TextField required label="Username" variant="outlined"
                value={username} onChange={(e) => setUsername(e.currentTarget.value)}
            />}

        <TextField required type='email' label="Email" variant="outlined"
            value={email} onChange={(e) => setEmail(e.currentTarget.value)}
        />

        <TextField required type='password' label="Password" variant="outlined"
            value={password} onChange={(e) => setPassword(e.currentTarget.value)}
        />

        {register &&
            <TextField required type='password' label="Confirm Password" variant="outlined"
                value={confirmPassword} onChange={(e) => setConfirmPassword(e.currentTarget.value)}
                error={confirmPassword !== '' && confirmPassword !== password}
                helperText={confirmPassword !== '' && confirmPassword !== password ? "Passwords do not match." : ''}
            />}

        <Button type="submit" variant="contained" startIcon={register ? <HowToRegIcon /> : <LoginIcon />} disabled={submitting}>
            {register ? "Register" : "Login"}
        </Button>

        <Typography>
            {register ? "Already have an account? " : "Don't have an account? "}
            <Link component="button" type="button" onClick={() => { setRegister(!register); setMessage(''); setConfirmPassword(''); }}>
                {register ? "Login here" : "Register here"}
            </Link>
        </Typography>
    </Stack>
}
