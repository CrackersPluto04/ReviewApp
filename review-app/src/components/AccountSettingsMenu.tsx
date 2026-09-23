import { Alert, IconButton, Menu, MenuItem, Snackbar, Tooltip } from "@mui/material";
import ManageAccountsIcon from '@mui/icons-material/ManageAccounts';
import { useState } from "preact/hooks";
import { userService } from "../services/UserService";
import { ChangeUsernameDialog } from "./ChangeUsernameDialog";
import { ChangeEmailDialog } from "./ChangeEmailDialog";
import { ChangePasswordDialog } from "./ChangePasswordDialog";

type AccountSettingsMenuProps = {
    username: string;
    onChangeUsername: (newUsername: string) => Promise<{ success: boolean, message?: string }>;
};

type AccountDialog = 'username' | 'email' | 'password' | null;

export function AccountSettingsMenu({ username, onChangeUsername }: AccountSettingsMenuProps) {
    const [anchorEl, setAnchorEl] = useState<null | HTMLElement>(null);
    const [openDialog, setOpenDialog] = useState<AccountDialog>(null);
    const [toastMessage, setToastMessage] = useState('');

    const handleOpenDialog = (dialog: AccountDialog) => {
        setAnchorEl(null);
        setOpenDialog(dialog);
    };

    const handleChangeUsername = async (newUsername: string) => {
        const result = await onChangeUsername(newUsername);
        if (result.success)
            setOpenDialog(null);

        return result;
    };

    const handleChangeEmail = async (newEmail: string, currentPassword: string) => {
        const result = await userService.changeEmail(newEmail, currentPassword);
        if (result.success) {
            setOpenDialog(null);
            setToastMessage('Email changed successfully.');
        }

        return result;
    };

    const handleChangePassword = async (currentPassword: string, newPassword: string) => {
        const result = await userService.changePassword(currentPassword, newPassword);
        if (result.success) {
            setOpenDialog(null);
            setToastMessage('Password changed successfully.');
        }

        return result;
    };

    return <>
        <Tooltip title="Account settings">
            <IconButton size="small" onClick={(e: any) => setAnchorEl(e.currentTarget)}>
                <ManageAccountsIcon fontSize="small" />
            </IconButton>
        </Tooltip>

        <Menu anchorEl={anchorEl} open={Boolean(anchorEl)} onClose={() => setAnchorEl(null)}>
            <MenuItem onClick={() => handleOpenDialog('username')}>Change username</MenuItem>
            <MenuItem onClick={() => handleOpenDialog('email')}>Change email</MenuItem>
            <MenuItem onClick={() => handleOpenDialog('password')}>Change password</MenuItem>
        </Menu>

        <ChangeUsernameDialog
            open={openDialog === 'username'}
            onClose={() => setOpenDialog(null)}
            currentUsername={username}
            onSave={handleChangeUsername}
        />

        <ChangeEmailDialog
            open={openDialog === 'email'}
            onClose={() => setOpenDialog(null)}
            onSave={handleChangeEmail}
        />

        <ChangePasswordDialog
            open={openDialog === 'password'}
            onClose={() => setOpenDialog(null)}
            onSave={handleChangePassword}
        />

        <Snackbar open={!!toastMessage} autoHideDuration={4000} onClose={() => setToastMessage('')}>
            <Alert severity="success" variant="filled" sx={{ width: '100%' }}>
                {toastMessage}
            </Alert>
        </Snackbar>
    </>
}
