import { useSensors, useSensor, PointerSensor, KeyboardSensor, DragEndEvent, closestCenter, DndContext } from "@dnd-kit/core";
import { sortableKeyboardCoordinates, arrayMove, verticalListSortingStrategy, SortableContext } from "@dnd-kit/sortable";
import { Box, CircularProgress, Divider, IconButton, Typography } from "@mui/material";
import EditIcon from '@mui/icons-material/Edit';
import DeleteIcon from '@mui/icons-material/Delete';
import { useState, useEffect } from "preact/hooks";
import { collectionService } from "../services/CollectionService";
import { CollectionWithMediasDto, CollectionMediaDto, CollectionDto } from "../types/types";
import { SortableMediaCard } from "./SortableMediaCard";
import { CreateEditCollectionDialog } from "./CreateEditCollectionDialog";

type CollectionDetailsProps = {
    collectionId: number;
    /** Called after the collection was edited, so the parent's list can refresh its name and visibility. */
    onCollectionEdited: () => void;
    /** Called after the collection was deleted. */
    onCollectionDeleted: () => void;
    /** Called after a title was removed, so the parent can refresh the item counts. */
    onMediaRemoved: () => void;
}

/**
 * One collection with its titles in the saved order. The owner can reorder by drag and drop, edit and delete it
 * (the default Favourites collection is permanent, so it has no edit or delete buttons) and remove titles;
 * other viewers see it read-only. A drop updates the list at once and sends the move to the API in the
 * background, with the moved title's final index; a failed move is not reported, and the order is correct again
 * after the next load. The pointer has to move 5 px before a drag starts, so a click on a card is not a drag.
 */
export function CollectionDetails({ collectionId, onCollectionEdited, onCollectionDeleted, onMediaRemoved }: CollectionDetailsProps) {
    const [data, setData] = useState<CollectionWithMediasDto | null>(null);
    const [mediaItems, setMediaItems] = useState<CollectionMediaDto[]>([]);

    const [loading, setLoading] = useState(true);
    const [errorMessage, setErrorMessage] = useState<string>('');
    const [isEditDialogOpen, setIsEditDialogOpen] = useState(false);

    const sensors = useSensors(
        useSensor(PointerSensor, { activationConstraint: { distance: 5 } }), // Requires moving 5px before drag starts (prevents accidental drags on clicks)
        useSensor(KeyboardSensor, { coordinateGetter: sortableKeyboardCoordinates })
    );

    useEffect(() => {
        fetchDetails();
    }, [collectionId]);

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


    const handleDragEnd = (event: DragEndEvent) => {
        const { active, over } = event;

        if (over && active.id !== over.id) {
            setMediaItems((items) => {
                const oldIndex = items.findIndex((item) => item.media.id === active.id);
                const newIndex = items.findIndex((item) => item.media.id === over.id);

                const draggedMedia = items[oldIndex];

                // Instant UI update
                const newArray = arrayMove(items, oldIndex, newIndex);

                // Background sync
                collectionService.reorderMedia(collectionId, draggedMedia.dbMediaID, newIndex);

                return newArray;
            });
        }
    };

    const handleCollectionEdited = async (updatedColl: CollectionDto) => {
        setData((prevData) => {
            if (!prevData) return null;

            return {
                ...prevData,
                collection: updatedColl
            };
        });

        onCollectionEdited();
    }

    const handleCollectionDelete = async () => {
        if (!globalThis.confirm("Are you sure you want to delete this collection? This cannot be undone.")) return;

        const result = await collectionService.deleteCollection(collectionId);
        if (result.success)
            onCollectionDeleted();
        else
            setErrorMessage(result.message);
    };

    const handleMediaRemoved = async (mediaType: number, externalApiId: string) => {
        const result = await collectionService.removeMediaFromCollection(collectionId, mediaType, externalApiId);
        if (result.success) {
            setMediaItems((prevItems) => prevItems.filter(item => item.media.mediaType !== mediaType || item.media.externalApiID !== externalApiId));

            setData((prevData) => prevData ? {
                ...prevData,
                collection: {
                    ...prevData.collection,
                    mediaCount: prevData.collection.mediaCount - 1
                }
            } : null);

            onMediaRemoved();
        } else
            setErrorMessage(result.message);
    }

    if (loading) {
        return <Box sx={{ display: 'flex', justifyContent: 'center', alignItems: 'center', height: '50vh' }}>
            <CircularProgress />
        </Box>
    }

    if (!data || errorMessage)
        return <Typography color="error">{errorMessage}</Typography>;

    const { collection } = data;
    const isOwner = collection.isOwner;

    return <Box sx={{ display: 'flex', flexDirection: 'column', height: '100%' }}>
        <Box sx={{ display: 'flex', justifyContent: 'space-between', alignItems: 'flex-start', mb: 2 }}>
            <Box>
                <Typography variant="h4" fontWeight="bold">
                    {collection.name}
                </Typography>

                <Typography variant="body1" color="text.secondary" sx={{ mt: 1 }}>
                    {collection.visibilityLevel === 0 ? 'Private' : collection.visibilityLevel === 1 ? 'Public' : 'Followers Only'}
                    {' • '} Created: {collection.createdAt}
                    {' • '} {mediaItems.length} items
                </Typography>
            </Box>

            {/* Owner action buttons, Favourites is permanent */}
            {isOwner && !collection.isDefault && (
                <Box>
                    <IconButton color="primary" onClick={() => setIsEditDialogOpen(true)}>
                        <EditIcon />
                    </IconButton>

                    <IconButton color="error" onClick={handleCollectionDelete}>
                        <DeleteIcon />
                    </IconButton>
                </Box>
            )}
        </Box>

        <Divider sx={{ mb: 3 }} />

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
                <DndContext sensors={sensors} collisionDetection={closestCenter} onDragEnd={handleDragEnd}>
                    <SortableContext items={mediaItems.map(m => m.media.id)} strategy={verticalListSortingStrategy}>
                        <Box sx={{
                            display: 'flex',
                            flexDirection: 'column'
                        }}>
                            {mediaItems.map((item) => (
                                <SortableMediaCard
                                    key={item.media.id}
                                    media={item.media}
                                    collectionId={collectionId}
                                    onRemove={handleMediaRemoved}
                                    isOwner={isOwner}
                                />
                            ))}
                        </Box>
                    </SortableContext>
                </DndContext>
            )}
        </Box>

        <CreateEditCollectionDialog
            open={isEditDialogOpen}
            onClose={() => setIsEditDialogOpen(false)}
            onSuccess={handleCollectionEdited}
            collection={collection}
        />
    </Box>
}