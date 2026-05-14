import { Box, Button, CircularProgress, List, ListItemButton, ListItemText, Paper, Typography } from "@mui/material";
import { useState, useEffect } from "preact/hooks";
import { useParams } from "react-router-dom";
import { userService } from "../services/UserService";
import { CollectionDto } from "../types/types";
import { CreateCollectionDialog } from "./CreateCollectionDialog";
import { CollectionDetails } from "./CollectionDetails";

export function CollectionsTab() {
    const { username } = useParams();

    const [collections, setCollections] = useState<CollectionDto[]>([]);
    const [selectedCollectionId, setSelectedCollectionId] = useState<number | null>(null);

    const [loading, setLoading] = useState(true);
    const [errorMessage, setErrorMessage] = useState<string>('');
    const [isCreateDialogOpen, setIsCreateDialogOpen] = useState(false);

    useEffect(() => {
        fetchCollections();
    }, [username]);

    const fetchCollections = async () => {
        if (!username) return;

        setLoading(true);
        setErrorMessage('');

        const result = await userService.getUserCollections(username);
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
        // Re-fetch the sidebar so the new collection appears
        await fetchCollections();
        // Instantly switch the user's view to their brand new collection!
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
            {collections[0]?.isOwner && (
                <Button variant="contained" color="primary" fullWidth onClick={() => setIsCreateDialogOpen(true)}>
                    + Create Collection
                </Button>
            )}

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
                    onCollectionDeleted={handleCollectionDeleted}
                />
                // <Typography variant="h6">Loading Collection #{selectedCollectionId} details...</Typography>
            ) : (
                <Box sx={{ display: 'flex', height: '100%', justifyContent: 'center', alignItems: 'center', color: 'text.secondary' }}>
                    <Typography>Select a collection to view its contents.</Typography>
                </Box>
            )}
        </Paper>

        {/* CREATE COLLECTION DIALOG */}
        <CreateCollectionDialog
            open={isCreateDialogOpen}
            onClose={() => setIsCreateDialogOpen(false)}
            onSuccess={handleCollectionCreated}
        />
    </Box>
}