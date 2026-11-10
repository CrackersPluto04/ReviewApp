import { useEffect, useState } from "preact/hooks";
import { useNavigate } from "react-router-dom";
import { UserCompactDto } from "../types/types";
import { userService } from "../services/UserService";
import { Autocomplete, Box, Avatar, Typography, TextField, InputAdornment, CircularProgress } from "@mui/material";
import SearchIcon from '@mui/icons-material/Search';

/**
 * The header's "Find users" box: an autocomplete that lists users by username as the user types, and opens the
 * chosen one's profile. Requests are debounced, so the API is only asked once typing pauses.
 */
export function UserSearchBar() {
    const navigate = useNavigate();

    const [open, setOpen] = useState(false);
    const [options, setOptions] = useState<UserCompactDto[]>([]);
    const [loading, setLoading] = useState(false);
    const [inputValue, setInputValue] = useState('');

    // Waits for 300 ms without typing before asking the API
    useEffect(() => {
        const delayDebounceFn = setTimeout(async () => {
            if (inputValue.trim()) {
                setLoading(true);

                const result = await userService.searchUsers(inputValue);
                setOptions(result.success ? result.data : []);

                setLoading(false);
            } else {
                setOptions([]);
            }
        }, 300);

        return () => clearTimeout(delayDebounceFn);
    }, [inputValue]);

    return <Autocomplete
        sx={{ width: 250 }} size="small" open={open}

        onOpen={() => setOpen(true)}
        onClose={() => setOpen(false)}
        inputValue={inputValue}
        onInputChange={(_, newInputValue) => setInputValue(newInputValue)}
        options={options}
        loading={loading}

        getOptionLabel={(option) => option.username}
        // The API already filtered the list, so Autocomplete's own filtering is switched off
        filterOptions={(x) => x}

        onChange={(_, selectedUser) => {
            if (selectedUser) {
                navigate(`/profile/${encodeURIComponent(selectedUser.username)}/overview`);
                setInputValue('');
                setOpen(false);
            }
        }}

        renderOption={(props, option) => (
            <Box component="li" {...props as any} sx={{ display: 'flex', gap: 2, alignItems: 'center' }}>
                <Avatar src={option.profilePictureUrl} sx={{ width: 28, height: 28 }}>
                    {option.username.charAt(0).toUpperCase()}
                </Avatar>
                <Typography variant="body2" fontWeight="bold">
                    {option.username}
                </Typography>
            </Box>
        )}

        renderInput={(params) => (
            <TextField
                {...params as any}
                placeholder="Find users..."
                sx={{ bgcolor: 'background.paper', borderRadius: 1 }}
                slotProps={{
                    input: {
                        ...params.InputProps,
                        startAdornment: (
                            <InputAdornment position="start">
                                <SearchIcon fontSize="small" />
                            </InputAdornment>
                        ),
                        endAdornment: (
                            <>
                                {loading ? <CircularProgress color="inherit" size={20} /> : null}
                                {params.InputProps.endAdornment}
                            </>
                        ),
                    }
                }}
            />
        )}
    />
}