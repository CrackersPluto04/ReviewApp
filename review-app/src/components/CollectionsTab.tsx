import { Box, CircularProgress, FormControl, IconButton, List, ListItemButton, ListItemText, MenuItem, Paper, Select, Typography } from "@mui/material";
import AddIcon from "@mui/icons-material/Add"
import { useState, useEffect } from "preact/hooks";
import { useParams } from "react-router-dom";
import { userService } from "../services/UserService";
import { CollectionDto } from "../types/types";
import { CreateEditCollectionDialog } from "./CreateEditCollectionDialog";
import { CollectionDetails } from "./CollectionDetails";

export function CollectionsTab() {
    const { username } = useParams();

    const [collections, setCollections] = useState<CollectionDto[]>([]);
    const [selectedCollectionId, setSelectedCollectionId] = useState<number | null>(null);
    const [sortBy, setSortBy] = useState("createdAt_asc");

    const [loading, setLoading] = useState(true);
    const [errorMessage, setErrorMessage] = useState<string>('');
    const [isCreateDialogOpen, setIsCreateDialogOpen] = useState(false);

    useEffect(() => {
        fetchCollections();
    }, [username, sortBy]);

    const fetchCollections = async () => {
        if (!username) return;

        setLoading(true);
        setErrorMessage('');

        const result = await userService.getUserCollections(username, sortBy);
        if (result.success) {
            setCollections(result.data);
            // Auto-select the first collection if they have one
            if (result.data.length > 0 && !selectedCollectionId)
                setSelectedCollectionId(result.data[0].id);
        } else
            setErrorMessage(result.message);

        setLoading(false);
    };

    const handleCollectionCreated = async (newId: number) => {
        await fetchCollections();
        setSelectedCollectionId(newId);
    };

    const handleCollectionDeleted = async () => {
        setSelectedCollectionId(null);
        await fetchCollections();
    };

    if (loading) {
        return <Box sx={{ display: 'flex', justifyContent: 'center', alignItems: 'center', height: '50vh' }}>
            <CircularProgress />
        </Box>
    }

    return <Box sx={{ display: 'grid', gridTemplateColumns: '300px 1fr', gap: 3, alignItems: 'start' }}>
        {/* LEFT SIDEBAR */}
        <Box sx={{ display: 'flex', flexDirection: 'column', gap: 2 }}>
            {/* Collections list header */}
            <Box sx={{ display: 'flex', justifyContent: 'space-between', alignItems: 'center', px: 1 }}>
                <FormControl variant="standard" sx={{ minWidth: 120 }}>
                    <Select
                        value={sortBy}
                        onChange={(e) => setSortBy(String((e.target as HTMLInputElement).value))}
                        disableUnderline
                        sx={{ fontSize: '0.875rem', fontWeight: 'bold' }}
                    >
                        <MenuItem value="createdAt_desc">Newest First</MenuItem>
                        <MenuItem value="createdAt_asc">Oldest First</MenuItem>
                        <MenuItem value="name_asc">Name (A-Z)</MenuItem>
                        <MenuItem value="name_desc">Name (Z-A)</MenuItem>
                    </Select>
                </FormControl>

                {collections[0]?.isOwner && (
                    <IconButton
                        color="primary"
                        size="medium"
                        onClick={() => setIsCreateDialogOpen(true)}
                        sx={{ border: '1px solid', borderColor: 'primary.main' }}
                    >
                        <AddIcon fontSize="small" />
                    </IconButton>
                )}
            </Box>

            {/* Collections list */}
            <Paper variant="outlined" sx={{ overflow: 'hidden' }}>
                <List disablePadding>
                    {collections.length === 0 ? (
                        <Typography p="2" textAlign="center" color={errorMessage ? 'error' : 'text.secondary'}>
                            {errorMessage || 'No collections yet.'}
                        </Typography>
                    ) : collections.map(col => (
                        <ListItemButton
                            key={col.id}
                            selected={selectedCollectionId === col.id}
                            onClick={() => setSelectedCollectionId(col.id)}
                            sx={{ borderBottom: '1px solid', borderColor: 'divider' }}
                        >
                            <ListItemText
                                primary={col.name}
                                secondary={`${col.mediaCount} items`}
                                slotProps={{
                                    primary: { fontWeight: selectedCollectionId === col.id ? 'bold' : 'normal', color: selectedCollectionId === col.id ? 'primary' : 'text.primary' }
                                }}
                            />
                        </ListItemButton>
                    ))}
                </List>
            </Paper>
        </Box>

        {/* RIGHT MAIN CONTENT AREA */}
        <Paper variant="outlined" sx={{ p: 3, minHeight: '500px', borderRadius: 2 }}>
            {selectedCollectionId ? (
                <CollectionDetails
                    collectionId={selectedCollectionId}
                    onCollectionEdited={async () => await fetchCollections()}
                    onCollectionDeleted={handleCollectionDeleted}
                    onMediaRemoved={async () => await fetchCollections()}
                />
            ) : (
                <Box sx={{ display: 'flex', height: '100%', justifyContent: 'center', alignItems: 'center', color: 'text.secondary' }}>
                    <Typography>Select a collection to view its contents.</Typography>
                </Box>
            )}
        </Paper>

        {/* CREATE COLLECTION DIALOG */}
        <CreateEditCollectionDialog
            open={isCreateDialogOpen}
            onClose={() => setIsCreateDialogOpen(false)}
            onSuccess={handleCollectionCreated}
        />
    </Box>
}