export const pendingKeyFor=userId=>`u1-checkout-${userId}`;
export const noteKeyFor=userId=>`u1-checkout-note-${userId}`;

export function parsePending(value){
    try{
        const approval=JSON.parse(value);
        return approval?.requestId&&Array.isArray(approval.lines)&&['sending','unknown'].includes(approval.status)?approval:null;
    }catch{return null}
}

export function createDraft(cart,note,requestId){
    return {status:'draft',requestId,note,lines:cart.items.map(p=>({productId:p.id,quantity:p.quantity,unitPrice:p.price,name:p.name}))};
}
