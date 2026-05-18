import { useSortable } from "@dnd-kit/sortable";
import { CSS } from "@dnd-kit/utilities";
import { MediaDto } from "../types/types";
import { MediaCard } from "./MediaCard";

type SortableMediaCardProps = {
    media: MediaDto;
    collectionId?: number;
    onRemove?: (mediaType: number, externalApiId: string) => void;
    isOwner: boolean;
}

export function SortableMediaCard({ media, collectionId, onRemove, isOwner }: SortableMediaCardProps) {
    const {
        attributes,
        listeners,
        setNodeRef,
        transform,
        transition,
        isDragging,
    } = useSortable({
        id: media.id,
        disabled: !isOwner
    });

    const style = {
        transform: CSS.Transform.toString(transform),
        transition,
        opacity: isDragging ? 0.5 : 1,
        cursor: isOwner ? 'grab' : 'default',
        zIndex: isDragging ? 999 : 1,
    };

    return <div ref={setNodeRef} style={style} {...(attributes as any)} {...(listeners as any)}>
        <MediaCard media={media} collectionId={collectionId} onRemove={onRemove} isOwner={isOwner} />
    </div>
}