import { useSensors, useSensor, PointerSensor, KeyboardSensor, DragEndEvent, closestCenter, DndContext } from "@dnd-kit/core";
import { sortableKeyboardCoordinates, arrayMove, verticalListSortingStrategy, SortableContext } from "@dnd-kit/sortable";
import { Box, CircularProgress, Divider, IconButton, Typography } from "@mui/material";
import EditIcon from '@mui/icons-material/Edit';
import DeleteIcon from '@mui/icons-material/Delete';
import { useState, useEffect } from "preact/hooks";
import { collectionService } from "../services/CollectionService";
import { CollectionWithMediasDto, CollectionMediaDto } from "../types/types";
import { SortableMediaCard } from "./SortableMediaCard";

type CollectionDetailsProps = {
    collectionId: number;
    onCollectionDeleted: () => void;
}

export function CollectionDetails({ collectionId, onCollectionDeleted }: CollectionDetailsProps) {
    const [data, setData] = useState<CollectionWithMediasDto | null>(null);
    const [mediaItems, setMediaItems] = useState<CollectionMediaDto[]>([]);
    const [loading, setLoading] = useState(true);
    const [errorMessage, setErrorMessage] = useState<string>('');

    // Set up drag-and-drop sensors
    const sensors = useSensors(
        useSensor(PointerSensor, { activationConstraint: { distance: 5 } }), // Requires moving 5px before drag starts (prevents accidental drags on clicks)
        useSensor(KeyboardSensor, { coordinateGetter: sortableKeyboardCoordinates })
    );

    // Fetch the collection details and items
    useEffect(() => {
        const fetchDetails = async () => {
            setLoading(true);
            setErrorMessage('');

            const result = await collectionService.getCollectionWithMedias(collectionId);
            if (result.success) {
                setData(result.data);
                setMediaItems(result.data.mediaItems);
            } else
                setErrorMessage(result.message);

            setLoading(false);
        };

        fetchDetails();
    }, [collectionId]);

    /* Handlers */

    // Handle Drag and Drop
    const handleDragEnd = (event: DragEndEvent) => {
        const { active, over } = event;

        if (over && active.id !== over.id) {
            setMediaItems((items) => {
                const oldIndex = items.findIndex((item) => item.media.externalApiID === active.id);
                const newIndex = items.findIndex((item) => item.media.externalApiID === over.id);

                // Instant UI update
                const newArray = arrayMove(items, oldIndex, newIndex);

                // Background sync
                collectionService.reorderMedia(collectionId, active.id as number, newIndex);

                return newArray;
            });
        }
    };

    // Handle Deletion
    const handleDelete = async () => {
        if (!globalThis.confirm("Are you sure you want to delete this collection? This cannot be undone.")) return;

        const result = await collectionService.deleteCollection(collectionId);
        if (result.success) onCollectionDeleted();
    };

    // Check for loading and error states
    if (loading) {
        return <Box sx={{ display: 'flex', justifyContent: 'center', alignItems: 'center', height: '50vh' }}>
            <CircularProgress />
        </Box>
    }

    if (!data || errorMessage)
        return <Typography color="error">{errorMessage}</Typography>;

    // Get collection detals and ownership status
    const { collection } = data;
    const isOwner = collection.isOwner;

    return <Box sx={{ display: 'flex', flexDirection: 'column', height: '100%' }}>
        {/* HEADER SECTION */}
        <Box sx={{ display: 'flex', justifyContent: 'space-between', alignItems: 'flex-start', mb: 2 }}>
            {/* Collection informations */}
            <Box>
                <Typography variant="h4" fontWeight="bold">
                    {collection.name}
                </Typography>

                <Typography variant="body1" color="text.secondary" sx={{ mt: 1 }}>
                    {collection.visibilityLevel === 0 ? 'Private' : collection.visibilityLevel === 1 ? 'Public' : 'Followers Only'}
                    {' • '} Created: {new Date(collection.createdAt).toLocaleDateString()}
                    {' • '} {mediaItems.length} items
                </Typography>
            </Box>

            {/* Owner action buttons */}
            {isOwner && (
                <Box>
                    <IconButton color="primary" onClick={() => alert("Wire up Edit Dialog here!")}>
                        <EditIcon />
                    </IconButton>
                    <IconButton color="error" onClick={handleDelete}>
                        <DeleteIcon />
                    </IconButton>
                </Box>
            )}
        </Box>

        <Divider sx={{ mb: 3 }} />

        {/* MEDIA GRID AREA */}
        <Box sx={{ flexGrow: 1 }}>
            {mediaItems.length === 0 ? (
                <Box sx={{ p: 5, textAlign: 'center', color: 'text.secondary', border: '1px dashed grey', borderRadius: 2 }}>
                    <Typography variant="h6">
                        This collection is empty.
                    </Typography>

                    {isOwner && <Typography variant="body2">
                        Search for movies or music to add them here!
                    </Typography>}
                </Box>
            ) : (
                // Drag and drop context
                <DndContext sensors={sensors} collisionDetection={closestCenter} onDragEnd={handleDragEnd}>
                    <SortableContext items={mediaItems.map(m => m.media.externalApiID)} strategy={verticalListSortingStrategy}>
                        <Box sx={{
                            display: 'flex',
                            flexDirection: 'column'
                        }}>
                            {mediaItems.map((item) => (
                                <SortableMediaCard
                                    key={item.media.externalApiID}
                                    media={item.media}
                                    isOwner={isOwner}
                                />
                            ))}
                        </Box>
                    </SortableContext>
                </DndContext>
            )}
        </Box>
    </Box>
}