import { Alert, Button, Link, Stack, TextField, Typography } from "@mui/material";
import LoginIcon from '@mui/icons-material/Login';
import HowToRegIcon from '@mui/icons-material/HowToReg';
import { useState } from "preact/hooks";
import { authService } from "../services/AuthService";
import { useLocation, useNavigate } from "react-router-dom";
import { useAuth } from "../context/AuthContext";

export function LoginPage() {
    const navigate = useNavigate();
    const location = useLocation();
    const { setIsLoggedIn } = useAuth();

    const [username, setUsername] = useState('');
    const [email, setEmail] = useState('');
    const [password, setPassword] = useState('');

    const [register, setRegister] = useState(false);
    const [message, setMessage] = useState('');
    const [success, setSuccess] = useState(true);

    const handleAuth = async (e: Event) => {
        e.preventDefault();

        const result = register ?
            await authService.register(username, email, password)
            :
            await authService.login(email, password);

        setSuccess(result.success);
        setMessage(result.message);

        if (!register && result.success) {
            setIsLoggedIn(true);

            const returnTo = location.state?.returnTo
            if (returnTo)
                navigate(returnTo.pathname, { state: returnTo.state });
            else
                navigate('/home');
        }
    }

    return <Stack spacing={2} alignItems="center">
        <Typography variant="h4" component="h1" gutterBottom>
            {register ? "Register Account" : "Login to your account"}
        </Typography>

        {message && <Alert severity={success ? "success" : "error"}>{message}</Alert>}

        {register &&
            <TextField required label="Username" variant="outlined"
                value={username} onChange={(e) => setUsername(e.currentTarget.value)} />}

        <TextField required type='email' label="Email" variant="outlined"
            value={email} onChange={(e) => setEmail(e.currentTarget.value)}
        />

        <TextField required type='password' label="Password" variant="outlined"
            value={password} onChange={(e) => setPassword(e.currentTarget.value)}
        />

        <Button variant="contained" startIcon={register ? <HowToRegIcon /> : <LoginIcon />}
            onClick={handleAuth}
        >
            {register ? "Register" : "Login"}
        </Button>

        <Typography>
            {register ? "Already have an account?" : "Don't have an account?"}
            <Link component="button" onClick={() => { setRegister(!register); setMessage(''); }}>
                {register ? "Login here" : "Register here"}
            </Link>
        </Typography>
    </Stack>
}